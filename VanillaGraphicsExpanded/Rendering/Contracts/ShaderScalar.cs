using System;
using System.Globalization;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>The scalar representations supported by shader configuration.</summary>
internal enum ShaderScalarType { Bool, Int, UInt, Float }

/// <summary>Centralizes exact scalar parsing, invariant source spelling and specialization encoding.</summary>
internal readonly record struct ShaderScalar
{
    public ShaderScalarType Type { get; }
    public uint Bits { get; }
    public string GlslType => Type switch
    {
        ShaderScalarType.Bool => "bool", ShaderScalarType.Int => "int",
        ShaderScalarType.UInt => "uint", _ => "float"
    };
    public string Canonical => Type switch
    {
        ShaderScalarType.Bool => Bits == 0 ? "0" : "1",
        ShaderScalarType.Int => unchecked((int)Bits).ToString(CultureInfo.InvariantCulture),
        ShaderScalarType.UInt => Bits.ToString(CultureInfo.InvariantCulture),
        _ => BitConverter.UInt32BitsToSingle(Bits).ToString("R", CultureInfo.InvariantCulture)
    };
    public string GlslLiteral => Type switch
    {
        ShaderScalarType.Bool => Bits == 0 ? "false" : "true",
        ShaderScalarType.UInt => Canonical + "u",
        ShaderScalarType.Float when !Canonical.Contains('.') && !Canonical.Contains('E') => Canonical + ".0",
        _ => Canonical
    };

    #region Conversion
    /// <summary>Creates a validated scalar and canonicalizes signed zero.</summary>
    private ShaderScalar(ShaderScalarType type, uint bits) { Type = type; Bits = bits; }

    /// <summary>Maps supported CLR types without widening every input through a floating-point type.</summary>
    public static ShaderScalarType TypeOf(Type type)
    {
        if (type.IsEnum) type = Enum.GetUnderlyingType(type);
        if (type == typeof(bool)) return ShaderScalarType.Bool;
        if (type == typeof(int)) return ShaderScalarType.Int;
        if (type == typeof(uint)) return ShaderScalarType.UInt;
        if (type == typeof(float)) return ShaderScalarType.Float;
        throw new ArgumentException($"Unsupported shader scalar type '{type}'. Expected bool, int, uint, float or a 32-bit enum.");
    }

    /// <summary>Encodes a typed value using its exact 32-bit representation.</summary>
    public static ShaderScalar From<T>(T value) where T : struct
    {
        var type = TypeOf(typeof(T));
        return type switch
        {
            ShaderScalarType.Bool => new(type, (bool)(object)value ? 1u : 0u),
            ShaderScalarType.Int => new(type, unchecked((uint)Convert.ToInt32(value, CultureInfo.InvariantCulture))),
            ShaderScalarType.UInt => new(type, Convert.ToUInt32(value, CultureInfo.InvariantCulture)),
            _ => FromFloat((float)(object)value)
        };
    }

    /// <summary>Rejects NaN/infinity and normalizes negative zero for stable equality and encoding.</summary>
    private static ShaderScalar FromFloat(float value)
    {
        if (!float.IsFinite(value)) throw new ArgumentException($"Nonfinite shader value '{value}' is invalid.");
        return new(ShaderScalarType.Float, BitConverter.SingleToUInt32Bits(value == 0 ? 0 : value));
    }

    /// <summary>Accepts equivalent integral decimal spellings without truncation or double conversion.</summary>
    public static ShaderScalar Parse(ShaderScalarType type, string text)
    {
        if (type == ShaderScalarType.Float)
            return FromFloat(float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));
        if (type == ShaderScalarType.Bool && bool.TryParse(text, out bool flag)) return From(flag);
        return type switch
        {
            ShaderScalarType.Bool => ParseBoolean(text),
            ShaderScalarType.Int => From(int.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)),
            ShaderScalarType.UInt => From(uint.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)),
            _ => throw new ArgumentException($"Unsupported shader scalar type '{type}'.")
        };
    }

    /// <summary>Accepts Boolean numeric spellings only when they are exactly zero or one.</summary>
    private static ShaderScalar ParseBoolean(string text)
    {
        // The integral parser rejects fractional inputs even below decimal/double precision.
        int value = int.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        return value switch { 0 => From(false), 1 => From(true),
            _ => throw new ArgumentException($"Shader value '{text}' is not a Boolean (0 or 1).") };
    }

    /// <summary>Orders values within one scalar domain for range checking.</summary>
    public int CompareTo(ShaderScalar other)
    {
        if (Type != other.Type) throw new ArgumentException("Cannot compare different shader scalar types.");
        return Type switch
        {
            ShaderScalarType.Int => unchecked((int)Bits).CompareTo(unchecked((int)other.Bits)),
            ShaderScalarType.Float => BitConverter.UInt32BitsToSingle(Bits).CompareTo(BitConverter.UInt32BitsToSingle(other.Bits)),
            _ => Bits.CompareTo(other.Bits)
        };
    }
    #endregion
}
