using System;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>A validated graphics realization borrowing one shader executable within its renderer's lifetime.</summary>
internal sealed class GraphicsPipeline : IDisposable
{
    private readonly GraphicsPipelineLifetime lifetime;
    private bool disposed;
    internal GraphicsPipelineDesc Description { get; }
    internal GpuProgram Shader { get; }
    internal ulong ExecutableRevision { get; }
    internal GpuPreparedBindings Bindings { get; }

    #region Public API
    /// <summary>Publishes only after shader readiness and all executable/description contracts have passed.</summary>
    internal GraphicsPipeline(GraphicsPipelineLifetime lifetime, GraphicsPipelineDesc description, GpuProgram shader)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(shader);
        lifetime.Validate();
        if (!shader.EnsureReady()) throw new InvalidOperationException("Graphics shader preparation failed.");
        if (shader.GraphicsIdentity != description.Shader || shader.GraphicsInterface == null)
            throw new InvalidOperationException("The installed shader selection does not match the graphics description.");
        PipelineDescriptionValidation.Validate(description, GpuSupport.Graphics);
        GraphicsInterfaceValidation.Validate(description, shader.GraphicsInterface);
        Bindings = shader.ProgramLayout.BinaryInterface?.PreparedBindings
            ?? throw new InvalidOperationException("The executable has no prepared resource contract.");
        this.lifetime = lifetime;
        Description = description;
        Shader = shader;
        ExecutableRevision = shader.ExecutableRevision;
    }

    /// <summary>Rejects stale or retired dependencies without reflecting, preparing, binding or submitting native work.</summary>
    internal void Validate()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lifetime.Validate();
        if (Shader.IsRetired || !Shader.IsLinked || Shader.RequiresPreparation || Shader.ExecutableRevision != ExecutableRevision)
            throw new InvalidOperationException("The graphics executable is retired or obsolete; prepare a replacement.");
    }

    /// <summary>Checks exact target compatibility while allowing different framebuffer identities and dimensions.</summary>
    internal void ValidateTargets(RenderTargetSignature targets)
    {
        Validate();
        if (Description.Targets != targets) throw new InvalidOperationException("Incompatible graphics target signature.");
    }

    /// <summary>Retires this managed realization without disposing its externally owned shader or bindings.</summary>
    public void Dispose() => disposed = true;
    #endregion
}
