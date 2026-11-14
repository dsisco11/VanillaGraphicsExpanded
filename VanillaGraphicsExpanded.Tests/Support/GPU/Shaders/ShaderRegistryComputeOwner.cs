using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Tests.Support.Rendering.Uniforms;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

/// <summary>Owns a test compute pipeline and retains its CPU input block across native executable generations.</summary>
internal sealed class ShaderRegistryComputeOwner : GpuComputeProgram
{
    #region Public API
    /// <summary>Provides the retained input block whose identity survives executable replacement.</summary>
    internal PackedUniformBuffer Block { get; }

    /// <summary>Adopts a feature-owned pipeline and creates its retained CPU input block.</summary>
    internal ShaderRegistryComputeOwner(ComputeShaderAsset pipeline) : base(pipeline) =>
        Block = OwnUniformBuffer(new PackedUniformBuffer(16));

    #endregion

    #region Private
    /// <summary>Uses no additional resources for lifecycle-only binding probes.</summary>
    protected override RHIResourceBindings CaptureBindingsCore() => new(Shader);
    #endregion
}
