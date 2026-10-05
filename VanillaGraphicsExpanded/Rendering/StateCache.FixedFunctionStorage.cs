using VanillaGraphicsExpanded.Rendering.Pipeline.State;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns category values separately from field-level native knowledge.</summary>
internal sealed partial class StateCache
{
    private DepthState depth;
    private DepthStateKnowledge depthKnown;
    private RasterizerState rasterizer;
    private RasterizerStateKnowledge rasterizerKnown;
    private PrimitiveAssemblyState assembly;
    private PrimitiveAssemblyStateKnowledge assemblyKnown;
    private DynamicDrawState dynamicState;
    private DynamicDrawStateKnowledge dynamicKnown;
    // Each output is a value struct; copying the array cannot alias mutable output payloads.
    private BlendState[] blend = System.Array.Empty<BlendState>();
    private BlendStateKnowledge[] blendKnown = System.Array.Empty<BlendStateKnowledge>();
    #region Public API
    /// <summary>Copies concrete output values; callers must retain resolved coverage separately.</summary>
    internal BlendState[] CopyBlendValues() => (BlendState[])blend.Clone();
    #endregion
}
