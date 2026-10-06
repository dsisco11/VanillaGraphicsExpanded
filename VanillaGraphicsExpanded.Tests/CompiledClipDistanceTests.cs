using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies output reflection against compiler-produced SPIR-V with and without debug information.</summary>
public sealed class CompiledClipDistanceTests
{
    #region Public API
    /// <summary>Reads actual built-in output array extents across supported producing stages.</summary>
    [Theory]
    [InlineData("absent", (int)ShaderStageKind.Vertex, 0)]
    [InlineData("present", (int)ShaderStageKind.Vertex, 3)]
    [InlineData("helper", (int)ShaderStageKind.Vertex, 3)]
    [InlineData("geometry", (int)ShaderStageKind.Geometry, 2)]
    [InlineData("evaluation", (int)ShaderStageKind.TessellationEvaluation, 1)]
    public void CompiledOutputsDoNotDependOnDebugNames(string fixture, int kind, int expected)
    {
        var stage = new ShaderStageContract("clip", "clip.glsl", (ShaderStageKind)kind, new GpuBindingContract());
        var selection = new ShaderStageSelection(stage, new Dictionary<string, ShaderScalar>());
        Assert.Equal(expected, CompiledClipDistance.Read(selection, Binary(fixture)));
    }

    /// <summary>Evaluates compiler-emitted arithmetic using the selected specialization rather than its default.</summary>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(5, 6)]
    public void SpecializationControlsCompiledArrayExtent(int selected, int expected)
    {
        var option = new ShaderOption<int>("count", 2);
        var stage = new ShaderStageContract("clip", "clip.glsl", ShaderStageKind.Vertex, new GpuBindingContract(),
            specializations: [new ShaderSpecialization(7, option)]);
        var selection = new ShaderStageSelection(stage, new Dictionary<string, ShaderScalar> { ["count"] = ShaderScalar.From(selected) });
        Assert.Equal(expected, CompiledClipDistance.Read(selection, Binary("specialized")));
        // Conditional specialization arithmetic is deliberately outside the supported evaluator subset.
        Assert.Null(CompiledClipDistance.Read(selection, Binary("conditional")));
    }

    /// <summary>Rejects malformed binaries and mismatched entry-point execution models without inventing coverage.</summary>
    [Fact]
    public void UnverifiableBinariesFailClosed()
    {
        var stage = new ShaderStageContract("clip", "clip.glsl", ShaderStageKind.Geometry, new GpuBindingContract());
        var selection = new ShaderStageSelection(stage, new Dictionary<string, ShaderScalar>());
        Assert.Null(CompiledClipDistance.Read(selection, Binary("present")));
        Assert.Null(CompiledClipDistance.Read(selection, [0, 1, 2]));
        Assert.Null(CompiledClipDistance.Read(selection, Binary("geometry")[..24]));
    }

    /// <summary>Each effective structural selection supplies its own compiled output declaration.</summary>
    [Fact]
    public void StructuralVariantsHaveIndependentCompiledExtents()
    {
        var option = new ShaderOption<bool>("clipping", false);
        var stage = new ShaderStageContract("clip", "clip.glsl", ShaderStageKind.Vertex, new GpuBindingContract(), structural: [option]);
        var disabled = new ShaderStageSelection(stage, new Dictionary<string, ShaderScalar> { ["clipping"] = ShaderScalar.From(false) });
        var enabled = new ShaderStageSelection(stage, new Dictionary<string, ShaderScalar> { ["clipping"] = ShaderScalar.From(true) });
        var binaries = new Dictionary<string, byte[]> { [disabled.BinaryPath] = Binary("absent"), [enabled.BinaryPath] = Binary("present") };
        Assert.NotEqual(disabled.BinaryPath, enabled.BinaryPath);
        Assert.Equal(0, CompiledClipDistance.Read(disabled, binaries[disabled.BinaryPath]));
        Assert.Equal(3, CompiledClipDistance.Read(enabled, binaries[enabled.BinaryPath]));
    }

    /// <summary>Only stores reachable through the selected entry point's call graph establish output coverage.</summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 3)]
    public void EntryPointReachabilityControlsOutputCoverage(bool callProducer, int expected)
    {
        var stage = new ShaderStageContract("clip", "clip.glsl", ShaderStageKind.Vertex, new GpuBindingContract());
        var selection = new ShaderStageSelection(stage, new Dictionary<string, ShaderScalar>());
        // Wrap the compiler-produced function with an empty entry point or an entry point that calls it.
        // This intentionally synthetic call-graph variation preserves the real output types and stores.
        uint[] original = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(Binary("present")).ToArray();
        var words = original.ToList();
        uint entry = 0, functionType = 0, returnType = 0;
        uint wrapper = words[3], label = wrapper + 1, result = wrapper + 2;
        words[3] += 3;
        for (int offset = 5; offset < original.Length; offset += (int)(original[offset] >> 16))
        {
            int opcode = (int)(original[offset] & 65535);
            if (opcode == 15) { entry = original[offset + 2]; words[offset + 2] = wrapper; }
            if (opcode == 54 && original[offset + 2] == entry)
            { returnType = original[offset + 1]; functionType = original[offset + 4]; }
        }
        words.AddRange([(5u << 16) | 54u, returnType, wrapper, 0, functionType, (2u << 16) | 248u, label]);
        if (callProducer) words.AddRange([(4u << 16) | 57u, returnType, result, entry]);
        words.AddRange([(1u << 16) | 253u, (1u << 16) | 56u]);
        byte[] binary = System.Runtime.InteropServices.MemoryMarshal.AsBytes(words.ToArray().AsSpan()).ToArray();
        Assert.Equal(expected, CompiledClipDistance.Read(selection, binary));
    }
    #endregion

    #region Private
    /// <summary>Loads an immutable fixture compiled by the repository-pinned shader compiler.</summary>
    private static byte[] Binary(string fixture)
    {
        using var source = typeof(CompiledClipDistanceTests).Assembly.GetManifestResourceStream(
            $"VanillaGraphicsExpanded.Tests.Fixtures.ClipDistance.{fixture}.spv")!;
        using var result = new MemoryStream();
        source.CopyTo(result);
        return result.ToArray();
    }
    #endregion
}
