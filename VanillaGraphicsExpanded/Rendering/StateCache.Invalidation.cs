using System;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns selective invalidation of cached knowledge without querying or changing native state.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Forgets only the selected categories and their dependent snapshots; makes no GL calls.</summary>
    /// <param name="states">Categories to invalidate, combined with bitwise OR; None is a no-op.</param>
    /// <exception cref="ArgumentOutOfRangeException">The mask contains undefined bits.</exception>
    public void Invalidate(EPipelineState states)
    {
        if (!EPipelineState.All.HasFlag(states)) throw new ArgumentOutOfRangeException(nameof(states));

        // Preserve the existing public invalidation groups while using unified category masks.
        if (states.HasFlag(EPipelineState.DepthRange)) depthKnown &= ~DepthStateKnowledge.DepthRange;
        if (states.HasFlag(EPipelineState.Blend)) ForgetBlendEquations();
        if (states.HasFlag(EPipelineState.RasterParameters)) rasterizerKnown &= ~(RasterizerStateKnowledge.CullMode | RasterizerStateKnowledge.FrontFace | RasterizerStateKnowledge.PolygonModes | RasterizerStateKnowledge.PolygonOffset | RasterizerStateKnowledge.DepthClamp | RasterizerStateKnowledge.RasterizerDiscard | RasterizerStateKnowledge.PolygonOffsetFill | RasterizerStateKnowledge.PolygonOffsetLine | RasterizerStateKnowledge.PolygonOffsetPoint | RasterizerStateKnowledge.ProgramPointSize);
        if (states.HasFlag(EPipelineState.Stencil)) stencilKnown = default;
        if (states.HasFlag(EPipelineState.Sampling)) { samplingKnown = default; sampleMasks.Clear(); }
        if (states.HasFlag(EPipelineState.OutputInterpretation)) outputKnown = default;
        if (states.HasFlag(EPipelineState.PrimitiveRestart)) assemblyKnown &= ~(PrimitiveAssemblyStateKnowledge.RestartIndex | PrimitiveAssemblyStateKnowledge.PrimitiveRestart | PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex);
        if (states.HasFlag(EPipelineState.ScissorRectangle)) dynamicKnown &= ~DynamicDrawStateKnowledge.Scissor;
        if (states.HasFlag(EPipelineState.BlendConstant)) dynamicKnown &= ~DynamicDrawStateKnowledge.BlendConstant;
        if (states.HasFlag(EPipelineState.ConfigurableRaster))
        {
            rasterizerKnown &= ~RasterizerStateKnowledge.ConfigurableRaster;
            clipDistancesKnown = 0;
        }
        if (states.HasFlag(EPipelineState.Depth)) depthKnown = default;
        if (states.HasFlag(EPipelineState.Blend)) { DirtyIndexedBlendEnable(); DirtyIndexedBlendFunc(); }
        if (states.HasFlag(EPipelineState.CullFace)) rasterizerKnown &= ~RasterizerStateKnowledge.CullEnabled;
        if (states.HasFlag(EPipelineState.ScissorTest)) rasterizerKnown &= ~RasterizerStateKnowledge.ScissorEnabled;
        if (states.HasFlag(EPipelineState.ColorMask))
            for (int i = 0; i < blendKnown.Length; i++) blendKnown[i] &= ~BlendStateKnowledge.WriteMask;
        if (states.HasFlag(EPipelineState.LineWidth)) rasterizerKnown &= ~RasterizerStateKnowledge.LineWidth;
        if (states.HasFlag(EPipelineState.PointSize)) rasterizerKnown &= ~RasterizerStateKnowledge.PointSize;
        if (states.HasFlag(EPipelineState.PatchVertices)) assemblyKnown &= ~PrimitiveAssemblyStateKnowledge.PatchVertices;
        if (states.HasFlag(EPipelineState.ProvokingVertex)) rasterizerKnown &= ~RasterizerStateKnowledge.ProvokingVertex;
        if (states.HasFlag(EPipelineState.Viewport)) dynamicKnown &= ~DynamicDrawStateKnowledge.Viewport;
        if (states.HasFlag(EPipelineState.ClearColor)) clearColorKnown = false;
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
        if (states.HasFlag(EPipelineState.PixelUnpack)) pixelUnpackState = null;
        if (states.HasFlag(EPipelineState.PixelPack)) pixelPackState = null;
    }
    #endregion
}
