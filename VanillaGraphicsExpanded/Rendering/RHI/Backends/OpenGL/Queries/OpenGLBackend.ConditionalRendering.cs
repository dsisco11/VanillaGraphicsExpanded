using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves conditional-render query consumption without CPU result readback or emulation.</summary>
internal sealed partial class OpenGLBackend
{
    private GpuQuery? conditionalQuery;
    private ulong conditionalRevision;
    #region Public API
    /// <inheritdoc />
    public void BeginConditionalRender(GpuQueryRef query, ConditionalRenderPolicy mode)
    {
        // Native callbacks must not reenter an unfinished execution-state transition.
        if (queryTransition) throw new InvalidOperationException("An auxiliary operation is already in progress.");
        queryTransition = true;
        try
        {
            var owner = RequireQuery(query);
            if (conditionalQuery is not null || cache.HasQueryCondition || owner.QueryKind is not (QueryKind.SamplesPassed or QueryKind.AnySamplesPassed or QueryKind.AnySamplesPassedConservative)
                || !queryExecutions.GetOrCreateValue(owner).Issued)
                throw new InvalidOperationException("Conditional rendering requires an issued occlusion query and no active condition.");
            var nativeMode = mode switch
            {
                ConditionalRenderPolicy.Wait => ConditionalRenderType.QueryWait,
                ConditionalRenderPolicy.NoWait => ConditionalRenderType.QueryNoWait,
                ConditionalRenderPolicy.RegionWait => ConditionalRenderType.QueryByRegionWait,
                ConditionalRenderPolicy.RegionNoWait => ConditionalRenderType.QueryByRegionNoWait,
                ConditionalRenderPolicy.WaitInverted => ConditionalRenderType.QueryWaitInverted,
                ConditionalRenderPolicy.NoWaitInverted => ConditionalRenderType.QueryNoWaitInverted,
                ConditionalRenderPolicy.RegionWaitInverted => ConditionalRenderType.QueryByRegionWaitInverted,
                ConditionalRenderPolicy.RegionNoWaitInverted => ConditionalRenderType.QueryByRegionNoWaitInverted,
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
            owner.RetainPassReference();
            try
            {
                OpenGLDriverCalls.BeginConditionalRender(owner.QueryId, nativeMode);
                RequireContext();
                cache.ObserveQueryCondition(owner.QueryId, (int)nativeMode);
                conditionalRevision = cache.QueryConditionRevision;
                conditionalQuery = owner;
            }
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
            try
            {
                if (!cache.IsQueryConditionCurrent(owner, conditionalRevision))
                    throw new InvalidOperationException("The original conditional interval was ended or replaced externally.");
                OpenGLDriverCalls.EndConditionalRender(); RequireContext(); cache.EndQueryCondition();
            }
            finally { conditionalQuery = null; owner.ReleasePassReference(); }
        }
        finally { queryTransition = false; }
    }
    #endregion
}
