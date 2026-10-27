using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Spirv;
using Native = OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks semantic schema decoding and preparation without admitting a native context.</summary>
public sealed class SemanticResourceSchemaTests
{
    #region Public API
    /// <summary>All supported immutable permissions survive native encoding and decoding, including sparse storage.</summary>
    [Fact]
    public void BufferPermissionsRoundTripWithoutTokenIdentity()
    {
        foreach (var flag in Enum.GetValues<StorageAccess>())
            Assert.Equal(flag, OpenGLBufferValues.Storage(OpenGLBufferValues.Storage(flag)));
    }

    /// <summary>Mutable usage decodes to semantic facts independently of native enum values.</summary>
    [Fact]
    public void BufferUsageDecodesEverySupportedHint()
    {
        foreach (var usage in Enum.GetValues<BufferUsage>().Where(value => value != BufferUsage.Undefined))
            Assert.Equal(usage, OpenGLBufferValues.Usage((OpenTK.Graphics.OpenGL.BufferUsageHint)OpenGLBufferValues.Usage(usage)));
    }

    /// <summary>Authored shader resource values no longer carry native token numbers.</summary>
    [Fact]
    public void ShaderResourceValuesAreSemantic()
    {
        Assert.NotEqual((int)Native.ActiveUniformType.Sampler2D, (int)ShaderResourceType.Sampler2D);
        Assert.Equal(ShaderValueType.Sampler2D, OpenGLShaderValues.Value(Native.All.Sampler2D));
        Assert.Equal(ShaderValueType.DoubleMat3x4, OpenGLShaderValues.Value(Native.All.DoubleMat3x4));
        Assert.Equal(ShaderValueType.Unknown, OpenGLShaderValues.Value((Native.All)(-1)));
        Assert.Equal(ShaderStage.TessellationEvaluation, OpenGLShaderValues.Stage(Native.ShaderType.TessEvaluationShader));
    }

    /// <summary>Preparation consumes supplied type, unit and limit facts without context lookup.</summary>
    [Fact]
    public void SuppliedReflectionPreparesWithoutContextAdmission()
    {
        var contract = new GpuBindingContract();
        contract.Samplers.Add("source", new(2, true, ShaderType: ShaderResourceType.Sampler2D));
        var snapshot = new GpuProgramMetadata(true,
            [new(ProgramResourceKind.Uniform, 7, "source", Type: ShaderValueType.Sampler2D, Location: 4, ArraySize: 1)],
            [new(4, 2)]);
        var prepared = new GpuPreparedBindings(contract, snapshot, 8, 4);
        Assert.True(prepared.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "source")).Active);
        Assert.Throws<InvalidOperationException>(() => new GpuPreparedBindings(contract, snapshot, 2, 4));
        Assert.Throws<InvalidOperationException>(() => new GpuPreparedBindings(contract,
            snapshot with { Resources = [snapshot.Resources[0] with { Type = ShaderValueType.IntSampler2D }] }, 8, 4));
        Assert.Throws<InvalidOperationException>(() => new GpuPreparedBindings(contract,
            snapshot with { Resources = [snapshot.Resources[0] with { ArraySize = null }] }, 8, 4));
    }
    #endregion
}
