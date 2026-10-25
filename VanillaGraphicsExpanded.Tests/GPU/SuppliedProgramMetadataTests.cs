using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks preparation solely against immutable supplied executable facts.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SuppliedProgramMetadataTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Canonical resource indices and fixed array units survive query-free preparation.</summary>
    [Fact]
    public void ValidSnapshotPreservesIndicesAndArrayActivity()
    {
        EnsureContextValid();
        using var capture = new ProgramAcquisitionCapture();
        var prepared = new GpuPreparedBindings(Contract(), Snapshot());
        var entry = prepared.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "inputs"));
        Assert.True(entry.Active);
        Assert.Equal(new[] { true, true }, entry.ActiveElements);
        Assert.Equal(7, prepared.UniformResourceLocation(12));
        Assert.Equal(-1, prepared.UniformResourceLocation(0));
        Assert.Equal(8, prepared.TextureUniformLocation(ShaderBindingKind.Sampler, 3));
        Assert.Equal(0, capture.Reads);
    }

    /// <summary>Missing or inconsistent supplied facts fail without attempting acquisition.</summary>
    [Theory]
    [InlineData("unavailable")]
    [InlineData("resources")]
    [InlineData("units")]
    [InlineData("property")]
    [InlineData("scalar")]
    [InlineData("assignment")]
    [InlineData("incompatible")]
    public void InvalidSnapshotRejectsWithoutAcquisition(string defect)
    {
        EnsureContextValid();
        var snapshot = Snapshot();
        var resource = snapshot.Resources[0];
        snapshot = defect switch
        {
            "unavailable" => snapshot with { ExecutableAvailable = false },
            "resources" => snapshot with { Resources = default },
            "units" => snapshot with { TextureUnits = default },
            "property" => snapshot with { Resources = [resource with { Properties = resource.Properties.Remove(ProgramProperty.ArraySize) }] },
            "scalar" => snapshot with { Resources = [resource with { Properties = resource.Properties.SetItem(ProgramProperty.ArraySize, []) }] },
            "assignment" => snapshot with { TextureUnits = [new(7, 2)] },
            "incompatible" => snapshot with { Resources = [resource with { Properties = resource.Properties.SetItem(ProgramProperty.Type, [(int)ActiveUniformType.IntSampler2D]) }] },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        using var capture = new ProgramAcquisitionCapture();
        Assert.Throws<InvalidOperationException>(() => new GpuPreparedBindings(Contract(), snapshot));
        Assert.Equal(0, capture.Reads);
    }
    /// <summary>Stage-split blocks preserve size and representative index, rejecting ambiguous sharing.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void BlockStageAndSizeFactsRemainAuthoritative(bool sameStage, bool differentSize)
    {
        EnsureContextValid();
        var contract = new GpuBindingContract();
        contract.RegisterUniformBlockBinding("Parameters", 1);
        var snapshot = new GpuProgramMetadata(true,
            [Block(8, 64, ProgramProperty.ReferencedByVertexShader),
             Block(14, differentSize ? 80 : 64, sameStage ? ProgramProperty.ReferencedByVertexShader : ProgramProperty.ReferencedByFragmentShader)], []);
        using var capture = new ProgramAcquisitionCapture();
        if (sameStage || differentSize)
            Assert.Throws<InvalidOperationException>(() => new GpuPreparedBindings(contract, snapshot));
        else
        {
            var prepared = new GpuPreparedBindings(contract, snapshot);
            Assert.Equal(8, prepared.BlockResourceIndex(ShaderBindingKind.UniformBlock, 1));
            Assert.Equal(64, prepared.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "Parameters")).MinimumSize);
        }
        Assert.Equal(0, capture.Reads);
    }
    /// <summary>Atomic slot activity comes from supplied buffer facts and duplicate slots remain invalid.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtomicBufferSlotsRejectAmbiguity(bool duplicate)
    {
        EnsureContextValid();
        var contract = new GpuBindingContract();
        contract.AtomicCounters.Add("counter", new(1, true));
        var resource = new GpuProgramResourceMetadata(ProgramInterface.AtomicCounterBuffer, 4, null,
            ImmutableDictionary<ProgramProperty, ImmutableArray<int>>.Empty.Add(ProgramProperty.BufferBinding, [1]));
        var snapshot = new GpuProgramMetadata(true, duplicate ? [resource, resource with { Index = 5 }] : [resource], []);
        using var capture = new ProgramAcquisitionCapture();
        if (duplicate) Assert.Throws<InvalidOperationException>(() => new GpuPreparedBindings(contract, snapshot));
        else Assert.True(new GpuPreparedBindings(contract, snapshot).Resolve(GpuBindingEntry.Identity(ShaderBindingKind.AtomicCounter, "counter")).Active);
        Assert.Equal(0, capture.Reads);
    }
    #endregion

    #region Private
    /// <summary>Builds all required block scalars with one active stage.</summary>
    private static GpuProgramResourceMetadata Block(int index, int size, ProgramProperty stage)
    {
        var values = new Dictionary<ProgramProperty, ImmutableArray<int>>
        {
            [ProgramProperty.BufferBinding] = [1], [ProgramProperty.BufferDataSize] = [size],
            [ProgramProperty.ReferencedByVertexShader] = [0], [ProgramProperty.ReferencedByTessControlShader] = [0],
            [ProgramProperty.ReferencedByTessEvaluationShader] = [0], [ProgramProperty.ReferencedByGeometryShader] = [0],
            [ProgramProperty.ReferencedByFragmentShader] = [0], [(ProgramProperty)All.ReferencedByComputeShader] = [0]
        };
        values[stage] = [1];
        return new(ProgramInterface.UniformBlock, index, "Parameters", values.ToImmutableDictionary());
    }
    /// <summary>Supplies a two-element floating texture contract with explicit fixed slots.</summary>
    private static GpuBindingContract Contract()
    {
        var contract = new GpuBindingContract();
        contract.Samplers.Add("inputs", new GpuBindingContract.Binding(2, true, 2, ShaderType: ShaderResourceType.Sampler2D));
        return contract;
    }

    /// <summary>Creates complete sampler facts whose native index differs from its array position.</summary>
    private static GpuProgramMetadata Snapshot() => new(true,
        [new(ProgramInterface.Uniform, 12, "inputs", new Dictionary<ProgramProperty, ImmutableArray<int>>
        {
            [ProgramProperty.Location] = [7],
            [ProgramProperty.Type] = [(int)ActiveUniformType.Sampler2D],
            [ProgramProperty.ArraySize] = [2]
        }.ToImmutableDictionary())], [new(7, 2), new(8, 3)]);
    #endregion
}
