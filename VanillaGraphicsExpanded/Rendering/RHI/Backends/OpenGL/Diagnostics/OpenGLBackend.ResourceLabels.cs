using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Labels admitted original allocations without querying native validity or changing ownership.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void LabelResource(GpuResource resource, string? name)
    {
#if DEBUG
        // Optional diagnostics reject stale/wrong-context owners before translating their identity.
        // No metadata recovery, binding, publication or native success probe is required.
        try
        {
            RequireContext();
            resource.RequireOriginalContext();
            if (!resource.IsValid) return;
            ObjectLabelIdentifier? kind = resource.Kind switch
            {
                GpuResourceKind.Buffer => ObjectLabelIdentifier.Buffer,
                GpuResourceKind.VertexArray => ObjectLabelIdentifier.VertexArray,
                GpuResourceKind.Texture => ObjectLabelIdentifier.Texture,
                GpuResourceKind.Framebuffer => ObjectLabelIdentifier.Framebuffer,
                GpuResourceKind.Renderbuffer => ObjectLabelIdentifier.Renderbuffer,
                GpuResourceKind.Query => ObjectLabelIdentifier.Query,
                GpuResourceKind.Program => ObjectLabelIdentifier.Program,
                GpuResourceKind.TransformFeedback => ObjectLabelIdentifier.TransformFeedback,
                GpuResourceKind.Sampler => ObjectLabelIdentifier.Sampler,
                GpuResourceKind.ProgramPipeline => ObjectLabelIdentifier.ProgramPipeline,
                GpuResourceKind.Shader => ObjectLabelIdentifier.Shader,
                _ => null
            };
            if (kind.HasValue) GlDebug.TryLabel(kind.Value, checked((int)resource.ResourceId), name);
        }
        catch { /* Optional labels never alter the operation's required failure behavior. */ }
#endif
    }
    #endregion
}
