using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves conditional-render query consumption without CPU result readback or emulation.</summary>
internal sealed partial class OpenGLBackend
{
    private GpuQuery? conditionalQuery;
    #region Public API
    /// <inheritdoc />
    public void BeginConditionalRender(GpuQueryRef query, RHIConditionalRenderMode mode)
    {
        // Native callbacks must not reenter an unfinished execution-state transition.
        if (queryTransition) throw new InvalidOperationException("An auxiliary operation is already in progress.");
        queryTransition = true;
        try
        {
            var owner = RequireQuery(query);
            if (conditionalQuery is not null || owner.QueryKind is not (RHIQueryKind.SamplesPassed or RHIQueryKind.AnySamplesPassed or RHIQueryKind.AnySamplesPassedConservative)
                || !queryExecutions.GetOrCreateValue(owner).Issued)
                throw new InvalidOperationException("Conditional rendering requires an issued occlusion query and no active condition.");
            var nativeMode = mode switch
            {
                RHIConditionalRenderMode.Wait => ConditionalRenderType.QueryWait,
                RHIConditionalRenderMode.NoWait => ConditionalRenderType.QueryNoWait,
                RHIConditionalRenderMode.RegionWait => ConditionalRenderType.QueryByRegionWait,
                RHIConditionalRenderMode.RegionNoWait => ConditionalRenderType.QueryByRegionNoWait,
                RHIConditionalRenderMode.WaitInverted => ConditionalRenderType.QueryWaitInverted,
                RHIConditionalRenderMode.NoWaitInverted => ConditionalRenderType.QueryNoWaitInverted,
                RHIConditionalRenderMode.RegionWaitInverted => ConditionalRenderType.QueryByRegionWaitInverted,
                RHIConditionalRenderMode.RegionNoWaitInverted => ConditionalRenderType.QueryByRegionNoWaitInverted,
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
            owner.RetainPassReference();
            try { GL.BeginConditionalRender(owner.QueryId, nativeMode); RequireContext(); conditionalQuery = owner; }
            catch { owner.ReleasePassReference(); throw; }
        }
        finally { queryTransition = false; }
    }
    /// <inheritdoc />
    public void EndConditionalRender()
    {
        // Native callbacks must not reenter an unfinished execution-state transition.
        if (queryTransition) throw new InvalidOperationException("An auxiliary operation is already in progress.");
        queryTransition = true;
        try
        {
            if (HasRetiredOrigin)
            {
                var retired = conditionalQuery ?? throw new InvalidOperationException("Conditional rendering is not active.");
                conditionalQuery = null;
                retired.ReleasePassReference();
                return;
            }
            RequireContext();
            var owner = conditionalQuery ?? throw new InvalidOperationException("Conditional rendering is not active.");
            try { GL.EndConditionalRender(); RequireContext(); }
            finally { conditionalQuery = null; owner.ReleasePassReference(); }
        }
        finally { queryTransition = false; }
    }
    #endregion
}
