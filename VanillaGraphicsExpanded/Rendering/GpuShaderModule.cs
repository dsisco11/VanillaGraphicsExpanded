using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns an existing native shader stage and its manager-published fixed-stage metadata.</summary>
internal sealed class GpuShaderModule : GpuResource<GpuShaderMetadata>
{
    private int shaderId;

    #region Public API
    /// <inheritdoc />
    public override nint ResourceId { get => shaderId; protected set => shaderId = (int)value; }
    /// <summary>Gets the native name for backend execution and explicit engine integration.</summary>
    public int ShaderId => shaderId;
    /// <summary>Gets the manager-published stage kind without native rediscovery.</summary>
    public ShaderType ShaderType => metadata.Type;
    /// <inheritdoc />
    public override void SetDebugName(string? debugName)
    {
#if DEBUG
        if (shaderId != 0) GlDebug.TryLabel(ObjectLabelIdentifier.Shader, shaderId, debugName);
#endif
    }
    #endregion

    #region Internal API
    /// <summary>Declares an unpublished stage recipient for backend-controlled allocation.</summary>
    internal GpuShaderModule() { }
    /// <summary>Adopts explicit external stage ownership and verifies its kind through manager acquisition.</summary>
    internal GpuShaderModule(int shaderId, ShaderType type)
    {
        this.shaderId = shaderId;
        GpuResourceManager.AcquireWrapperMetadata(this, GpuShaderMetadataReader.Instance);
        if (metadata.Type != type) throw new ArgumentException("Native shader stage differs from the adopted stage.", nameof(type));
    }
    #endregion

    #region Protected API
    /// <inheritdoc />
    protected override GpuResourceKind ResourceKind => GpuResourceKind.Shader;
    #endregion
}
