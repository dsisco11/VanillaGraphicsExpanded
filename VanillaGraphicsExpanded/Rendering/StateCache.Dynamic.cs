using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Applies declared dynamic values independently of static pipeline identity.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Establishes the declared viewport through the cached dynamic-state backend.</summary>
    internal void ApplyDynamic(in DynamicDrawState state)
    {
        ValidateBoundaryMutation(dynamic: DynamicDrawStateKnowledge.Viewport);
        SynchronizeContext();
        if (state.Width < 0 || state.Height < 0) throw new ArgumentOutOfRangeException(nameof(state));
        EnsureViewportLimits();
        var effective = state;
        effective.Width = Math.Min(state.Width, maxViewportWidth);
        effective.Height = Math.Min(state.Height, maxViewportHeight);
        if (dynamicKnown.HasFlag(DynamicDrawStateKnowledge.Viewport) && dynamicState.X == effective.X && dynamicState.Y == effective.Y
            && dynamicState.Width == effective.Width && dynamicState.Height == effective.Height) return;
        dynamicKnown &= ~DynamicDrawStateKnowledge.Viewport;
        GL.Viewport(effective.X, effective.Y, effective.Width, effective.Height);
        FixedFunctionCalls++;
        // Store effective native dimensions rather than the potentially clamped request.
        dynamicState = effective;
        dynamicKnown |= DynamicDrawStateKnowledge.Viewport;
    }
    #endregion
    #region Private
    private int maxViewportWidth, maxViewportHeight;
    /// <summary>Resolves immutable viewport limits once per context generation.</summary>
    private void EnsureViewportLimits()
    {
        if (maxViewportWidth != 0) return;
        int[] dimensions = QueryCapability(() =>
        {
            int[] value = new int[2];
            GL.GetInteger(GetPName.MaxViewportDims, value);
            return value;
        });
        if (dimensions[0] <= 0 || dimensions[1] <= 0) throw new InvalidOperationException("No current viewport capability.");
        maxViewportWidth = dimensions[0]; maxViewportHeight = dimensions[1];
        CapabilityQueries++;
    }
    #endregion
}
