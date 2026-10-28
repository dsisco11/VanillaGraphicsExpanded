using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

namespace OpenGLInterceptionGenerator.Bindings;

/// <summary>Reads binding IL operands without confusing token bytes with executable opcodes.</summary>
internal static class BindingInstructions
{
    private static readonly Dictionary<short, OpCode> codes = typeof(OpCodes).GetFields()
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    #region Internal API
    /// <summary>Decodes complete instructions and resolves integer literals used for native table slots.</summary>
    internal static List<(OpCode Code, int Value)> Read(byte[] bytes)
    {
        var result = new List<(OpCode, int)>();
        for (int offset = 0; offset < bytes.Length;)
        {
            short value = bytes[offset++];
            if (value == 0xfe) value = unchecked((short)(0xfe00 | bytes[offset++]));
            if (!codes.TryGetValue(value, out var code)) throw new FormatException("Unknown binding IL opcode.");
            int length = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineI or OperandType.ShortInlineVar or OperandType.ShortInlineBrTarget => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => checked(4 + 4 * BitConverter.ToInt32(bytes, offset)),
                _ => 4
            };
            if (length < 0 || offset + length > bytes.Length) throw new FormatException("Truncated binding IL operand.");
            int operand = length == 4 ? BitConverter.ToInt32(bytes, offset)
                : length == 1 ? (code == OpCodes.Ldc_I4_S ? (sbyte)bytes[offset] : bytes[offset]) : 0;
            if (code == OpCodes.Ldc_I4_M1) operand = -1;
            else if (value >= OpCodes.Ldc_I4_0.Value && value <= OpCodes.Ldc_I4_8.Value) operand = value - OpCodes.Ldc_I4_0.Value;
            result.Add((code, operand));
            offset += length;
        }
        return result;
    }

    /// <summary>Recognizes literal table indices rather than arbitrary numeric operands.</summary>
    internal static bool IsInteger(OpCode code) => code == OpCodes.Ldc_I4 || code == OpCodes.Ldc_I4_S
        || code.Value >= OpCodes.Ldc_I4_M1.Value && code.Value <= OpCodes.Ldc_I4_8.Value;
    #endregion
}
