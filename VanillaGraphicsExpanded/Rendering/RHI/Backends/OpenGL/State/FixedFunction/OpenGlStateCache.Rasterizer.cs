using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Owns Rasterizer transitions and independent field validity.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Establishes LineWidth, suppressing a known identical transition.</summary>
    internal void SetLineWidth(float width)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.LineWidth);
        if (!float.IsFinite(width) || width <= 0) throw new System.ArgumentOutOfRangeException(nameof(width));
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.LineWidth) && rasterizer.LineWidth == width) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.LineWidth;
        OpenGLStateEffects.LineWidth(width);
#if DEBUG
        FixedFunctionCalls++;
#endif
        rasterizer.LineWidth = width;
        rasterizerKnown |= RasterizerStateKnowledge.LineWidth;
    }
    /// <summary>Establishes PointSize, suppressing a known identical transition.</summary>
    internal void SetPointSize(float size)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.PointSize);
        if (!float.IsFinite(size) || size <= 0) throw new System.ArgumentOutOfRangeException(nameof(size));
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.PointSize) && rasterizer.PointSize == size) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.PointSize;
        OpenGLStateEffects.PointSize(size);
#if DEBUG
        FixedFunctionCalls++;
#endif
        rasterizer.PointSize = size;
        rasterizerKnown |= RasterizerStateKnowledge.PointSize;
    }
    /// <summary>Establishes ProvokingVertex, suppressing a known identical transition.</summary>
    internal void SetProvokingVertex(ProvokingVertexMode mode)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.ProvokingVertex);
        if (!System.Enum.IsDefined(mode)) throw new System.ArgumentOutOfRangeException(nameof(mode));
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.ProvokingVertex) && rasterizer.ProvokingVertex == mode) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.ProvokingVertex;
        OpenGLStateEffects.ProvokingVertex(mode);
#if DEBUG
        FixedFunctionCalls++;
#endif
        rasterizer.ProvokingVertex = mode;
        rasterizerKnown |= RasterizerStateKnowledge.ProvokingVertex;
    }
    #endregion
}
