using System;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns selective invalidation of cached knowledge without querying or changing native state.</summary>
internal sealed partial class GlStateCache
{
    #region Public API
    /// <summary>Forgets only the selected categories and their dependent snapshots; makes no GL calls.</summary>
    /// <param name="states">Categories to invalidate, combined with bitwise OR; None is a no-op.</param>
    /// <exception cref="ArgumentOutOfRangeException">The mask contains undefined bits.</exception>
    public void Invalidate(EPipelineState states)
    {
        if (!EPipelineState.All.HasFlag(states)) throw new ArgumentOutOfRangeException(nameof(states));

        if (states.HasFlag(EPipelineState.Depth))
        {
            depthTestEnabled = null;
            depthFunc = null;
            depthWriteMask = null;
        }
        if (states.HasFlag(EPipelineState.Blend))
        {
            // Global and indexed values describe overlapping native state, so forget them together.
            blendEnabled = null;
            blendFunc = null;
            DirtyIndexedBlendEnable();
            DirtyIndexedBlendFunc();
        }
        if (states.HasFlag(EPipelineState.CullFace)) cullFaceEnabled = null;
        if (states.HasFlag(EPipelineState.ScissorTest)) scissorTestEnabled = null;
        if (states.HasFlag(EPipelineState.ColorMask)) colorMask = null;
        if (states.HasFlag(EPipelineState.LineWidth)) lineWidth = null;
        if (states.HasFlag(EPipelineState.PointSize)) pointSize = null;
        if (states.HasFlag(EPipelineState.PatchVertices)) patchVertices = null;
        if (states.HasFlag(EPipelineState.ProvokingVertex)) provokingVertex = null;
        if (states.HasFlag(EPipelineState.Program)) currentProgram = null;
        if (states.HasFlag(EPipelineState.ProgramPipeline)) currentProgramPipeline = null;
        if (states.HasFlag(EPipelineState.VertexArray))
        {
            currentVao = null;
            elementArrayBufferByVao.Clear();
        }
        if (states.HasFlag(EPipelineState.FramebufferBindings))
        {
            currentFramebuffer = null;
            currentReadFramebuffer = null;
            currentDrawFramebuffer = null;
        }
        if (states.HasFlag(EPipelineState.RenderbufferBinding)) currentRenderbuffer = null;
        if (states.HasFlag(EPipelineState.TransformFeedback))
        {
            currentTransformFeedback = null;
            // Indexed feedback bindings belong to the selected feedback object.
            if (!states.HasFlag(EPipelineState.BufferBindings))
            {
                foreach (var key in indexedBufferBindings.Keys.Where(key => key.Target == BufferRangeTarget.TransformFeedbackBuffer).ToArray())
                    indexedBufferBindings.Remove(key);
            }
        }
        if (states.HasFlag(EPipelineState.ActiveTextureUnit)) activeTextureUnit = null;
        if (states.HasFlag(EPipelineState.TextureBindings)) textureBindingsByUnit = null;
        if (states.HasFlag(EPipelineState.SamplerBindings)) samplerBindingByUnit = null;
        if (states.HasFlag(EPipelineState.BufferBindings))
        {
            bufferBindingByTarget.Clear();
            indexedBufferBindings.Clear();
            elementArrayBufferByVao.Clear();
        }
        if (states.HasFlag(EPipelineState.ImageBindings)) imageBindings.Clear();
        if (states.HasFlag(EPipelineState.PixelPack)) pixelPackState = null;
    }
    #endregion
}
