using OpenTK.Mathematics;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Resolved incoming values for explicit coverage, independent of live cache knowledge.</summary>
internal sealed class PipelineStateSnapshot
{
    private readonly BlendState[] blend;
    internal PipelineStateCoverage Coverage { get; }
    internal DepthState Depth { get; }
    internal RasterizerState Rasterizer { get; }
    internal PrimitiveAssemblyState Assembly { get; }
    internal DynamicDrawState Dynamic { get; }
    internal Vector4 ClearColor { get; }
    internal int OutputCount => blend.Length;

    #region Public API
    /// <summary>Copies resolved category values; coverage alone determines which fields are meaningful.</summary>
    internal PipelineStateSnapshot(PipelineStateCoverage coverage,
        DepthState depth, RasterizerState rasterizer, PrimitiveAssemblyState assembly,
        DynamicDrawState dynamic, Vector4 clearColor, BlendState[] blend)
    {
        Coverage = coverage; Depth = depth; Rasterizer = rasterizer;
        Assembly = assembly; Dynamic = dynamic; ClearColor = clearColor;
        this.blend = (BlendState[])blend.Clone();
    }

    /// <summary>Returns an output value by copy, never exposing the retained array.</summary>
    internal BlendState BlendAt(int index) => blend[index];
    #endregion
}
