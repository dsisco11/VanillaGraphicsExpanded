using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns Rasterizer transitions and independent field validity.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Establishes LineWidth, suppressing a known identical transition.</summary>
    public void SetLineWidth(float width)
    {
        if (!float.IsFinite(width) || width <= 0) throw new System.ArgumentOutOfRangeException(nameof(width));
        SynchronizeContext();
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.LineWidth) && rasterizer.LineWidth == width) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.LineWidth;
        GL.LineWidth(width);
        FixedFunctionCalls++;
        rasterizer.LineWidth = width;
        rasterizerKnown |= RasterizerStateKnowledge.LineWidth;
    }
    /// <summary>Establishes PointSize, suppressing a known identical transition.</summary>
    public void SetPointSize(float size)
    {
        if (!float.IsFinite(size) || size <= 0) throw new System.ArgumentOutOfRangeException(nameof(size));
        SynchronizeContext();
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.PointSize) && rasterizer.PointSize == size) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.PointSize;
        GL.PointSize(size);
        FixedFunctionCalls++;
        rasterizer.PointSize = size;
        rasterizerKnown |= RasterizerStateKnowledge.PointSize;
    }
    /// <summary>Establishes ProvokingVertex, suppressing a known identical transition.</summary>
    public void SetProvokingVertex(ProvokingVertexMode mode)
    {
        if (!System.Enum.IsDefined(mode)) throw new System.ArgumentOutOfRangeException(nameof(mode));
        SynchronizeContext();
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.ProvokingVertex) && rasterizer.ProvokingVertex == mode) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.ProvokingVertex;
        GL.ProvokingVertex(mode);
        FixedFunctionCalls++;
        rasterizer.ProvokingVertex = mode;
        rasterizerKnown |= RasterizerStateKnowledge.ProvokingVertex;
    }
    #endregion
}
