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
        if (state.Width < 0 || state.Height < 0) throw new ArgumentOutOfRangeException(nameof(state));
        EnsureViewportLimits();
        var effective = state;
        effective.Width = Math.Min(state.Width, GpuSupport.MaxViewportWidth);
        effective.Height = Math.Min(state.Height, GpuSupport.MaxViewportHeight);
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
    /// <summary>Requires valid viewport limits from the shared capability owner.</summary>
    private static void EnsureViewportLimits()
    {
        GpuSupport.EnsureCurrentContext();
        if (GpuSupport.MaxViewportWidth <= 0 || GpuSupport.MaxViewportHeight <= 0)
            throw new InvalidOperationException("No current viewport capability.");
    }
    #endregion
}
