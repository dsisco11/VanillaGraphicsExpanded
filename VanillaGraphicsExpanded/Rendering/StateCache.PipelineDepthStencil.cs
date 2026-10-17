using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Applies relevant depth/aspect and paired stencil native groups for a complete draw.</summary>
internal sealed partial class StateCache
{
    #region Private
    /// <summary>Keeps draw test dependencies independent from write masks established by attachment clears.</summary>
    private void ApplyPipelineDepthStencil(GraphicsPipelineDesc pipeline, GraphicsDynamicState dynamics,
        GraphicsStateRelevance relevance)
    {
        var value = pipeline.DepthStencil;
        var depthGroups = relevance.Depth;
        if (depthGroups.HasFlag(DepthStateKnowledge.TestEnabled)) SetCapability(EnableCap.DepthTest, value.DepthTest);
        if (depthGroups.HasFlag(DepthStateKnowledge.Comparison)) SetDepthFunc(value.DepthComparison);
        if (depthGroups.HasFlag(DepthStateKnowledge.WriteEnabled)) SetDepthWriteMask(value.DepthWrite);
        if (depthGroups.HasFlag(DepthStateKnowledge.DepthRange)) SetDepthRange(value.DepthRangeNear, value.DepthRangeFar);
        var stencilGroups = relevance.Stencil;
        if (stencilGroups.HasFlag(StencilStateKnowledge.TestEnabled)) SetStencilTestEnabled(value.StencilTest);
        var refs = pipeline.Dynamics.HasFlag(DynamicPipelineState.StencilReference)
            ? dynamics.StencilReference!.Value : (Front: 0, Back: 0);
        if (stencilGroups.HasFlag(StencilStateKnowledge.FrontStencilFunction))
            SetStencilFunction(StencilFace.Front, value.Front.Comparison, refs.Front, value.Front.ReadMask);
        if (stencilGroups.HasFlag(StencilStateKnowledge.BackStencilFunction))
            SetStencilFunction(StencilFace.Back, value.Back.Comparison, refs.Back, value.Back.ReadMask);
        if (stencilGroups.HasFlag(StencilStateKnowledge.FrontStencilMask)) SetStencilWriteMask(StencilFace.Front, value.Front.WriteMask);
        if (stencilGroups.HasFlag(StencilStateKnowledge.BackStencilMask)) SetStencilWriteMask(StencilFace.Back, value.Back.WriteMask);
        if (stencilGroups.HasFlag(StencilStateKnowledge.FrontStencilOperation))
            SetStencilOperation(StencilFace.Front, value.Front.Fail, value.Front.DepthFail, value.Front.Pass);
        if (stencilGroups.HasFlag(StencilStateKnowledge.BackStencilOperation))
            SetStencilOperation(StencilFace.Back, value.Back.Fail, value.Back.DepthFail, value.Back.Pass);
    }
    #endregion
}
