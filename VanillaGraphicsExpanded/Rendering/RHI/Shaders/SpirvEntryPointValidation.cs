using System;
using System.Buffers.Binary;
using System.Text;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Checks the selected SPIR-V entry point's stage before native ingestion without reflecting resource metadata.</summary>
internal static class SpirvEntryPointValidation
{
    // SPIR-V grammar constants, independent of OpenGL tokens:
    // https://github.com/KhronosGroup/SPIRV-Headers/blob/main/include/spirv/unified1/spirv.core.grammar.json
    private const uint OpEntryPoint = 15;

    #region Internal API
    /// <summary>Bounds instruction traversal and requires an entry-point declaration matching both name and execution model.</summary>
    internal static void Require(ReadOnlySpan<byte> binary, ShaderStage stage, string entryPoint)
    {
        uint expectedModel = stage switch
        {
            ShaderStage.Vertex => 0,
            ShaderStage.TessellationControl => 1,
            ShaderStage.TessellationEvaluation => 2,
            ShaderStage.Geometry => 3,
            ShaderStage.Fragment => 4,
            ShaderStage.Compute => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };
        ReadOnlySpan<byte> expectedName = Encoding.UTF8.GetBytes(entryPoint);
        // The five-word header was validated by the immutable stage description. Valid declarations
        // precede function bodies, so successful selection stops without decoding shader instructions.
        for (int offset = 5 * sizeof(uint); offset < binary.Length;)
        {
            uint instruction = BinaryPrimitives.ReadUInt32LittleEndian(binary[offset..]);
            int bytes = checked((int)(instruction >> 16) * sizeof(uint));
            if (bytes == 0 || bytes > binary.Length - offset)
                throw new ArgumentException("Malformed SPIR-V instruction extent.", nameof(binary));
            if ((instruction & ushort.MaxValue) == OpEntryPoint)
            {
                if (bytes < 4 * sizeof(uint))
                    throw new ArgumentException("Malformed SPIR-V entry-point declaration.", nameof(binary));
                uint model = BinaryPrimitives.ReadUInt32LittleEndian(binary[(offset + sizeof(uint))..]);
                var name = binary.Slice(offset + 3 * sizeof(uint), bytes - 3 * sizeof(uint));
                int terminator = name.IndexOf((byte)0);
                if (terminator < 0)
                    throw new ArgumentException("Unterminated SPIR-V entry-point name.", nameof(binary));
                if (model == expectedModel && name[..terminator].SequenceEqual(expectedName)) return;
            }
            offset += bytes;
        }
        throw new ArgumentException($"SPIR-V specialization entry point '{entryPoint}' does not declare the requested {stage} stage.", nameof(binary));
    }
    #endregion
}
