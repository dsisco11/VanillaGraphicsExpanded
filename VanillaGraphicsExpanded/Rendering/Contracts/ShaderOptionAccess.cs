using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Reads typed selections from the shared normalized settings model.</summary>
internal static class ShaderOptionAccess
{
    #region Typed settings
    /// <summary>Checks key compatibility and decodes the exact declared scalar representation.</summary>
    public static T Get<T>(ShaderSettings settings, ShaderOption<T> option) where T : struct
    {
        if (!settings.Contract.FindOption(option.Name).Equivalent(option))
            throw new ArgumentException($"Program '{settings.Contract.Identity}' has an incompatible typed key '{option.Name}'.");
        var scalar = settings.Values[option.Name];
        object value = scalar.Type switch
        {
            ShaderScalarType.Bool => (object)(scalar.Bits != 0),
            ShaderScalarType.Int => unchecked((int)scalar.Bits),
            ShaderScalarType.UInt => scalar.Bits,
            _ => BitConverter.UInt32BitsToSingle(scalar.Bits)
        };
        return typeof(T).IsEnum ? (T)Enum.ToObject(typeof(T), value) : (T)value;
    }
    #endregion
}
