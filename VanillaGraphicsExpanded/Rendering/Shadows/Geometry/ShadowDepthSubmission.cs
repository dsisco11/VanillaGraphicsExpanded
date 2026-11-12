using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Orders complete native drawing, selective clears and whole-page publication without selection readback.</summary>
internal static class ShadowDepthSubmission
{
    #region Public API
    /// <summary>Submits a bounded replacement batch whose native owners certify complete current scene inputs.</summary>
    /// <remarks>Successful return means ordered submission, not that GPU admission succeeded. Consumer readiness
    /// must still use the page table. The caller retains every draw and input owner through submission and fences.</remarks>
    internal static void Submit(ShadowPagePool pages, ShadowGeometrySelection selection,
        IReadOnlyList<ShadowPageReplacement> replacements, IReadOnlyList<ShadowPreparedDraw> draws,
        ShadowSceneChanges changes, ShadowChangeBatch scene, Func<bool> completeInputsCurrent, double timestamp)
    {
        ArgumentNullException.ThrowIfNull(completeInputsCurrent);
        if (!pages.CanPrepareReplacements || replacements.Count == 0 || !scene.Complete ||
            !double.IsFinite(timestamp) || timestamp < 0 || !completeInputsCurrent() ||
            4L * replacements.Count + 2 > pages.OperationsRemaining)
            throw new InvalidOperationException("Complete replacement inputs or their reserved operation budget are unavailable.");
        selection.ValidateSubmission(draws, pages);
        var identities = new HashSet<ShadowPageKey>();
        long world = replacements[0].Request.WorldRevision;
        foreach (var replacement in replacements)
        {
            var request = replacement.Request;
            if (!identities.Add(request.Key) || request.WorldRevision != world || request.SceneEpoch != scene.Epoch ||
                request.SceneSequence != scene.Sequence || request.Timestamp > timestamp)
                throw new InvalidOperationException("Replacement requests do not share exact current scene dependencies.");
            selection.ValidateReplacement(replacement.View, request);
        }
        CheckScene(changes, scene, completeInputsCurrent);
        try
        {
            foreach (var replacement in replacements)
                if (!pages.AllocateSelected(replacement.Request, selection.Certificate))
                    throw new InvalidOperationException("Reserved replacement submission became unavailable.");
            if (!pages.FreezeWritableAssignments()) throw new InvalidOperationException("Replacement assignments could not be frozen.");
            foreach (var replacement in replacements)
                if (!pages.SelectReplacement(replacement.Request) || !pages.ClearReplacement())
                    throw new InvalidOperationException("Frozen replacement clear became unavailable.");
            var rhi = RHICommandContext.Current;
            using (var boundary = rhi.EnterBoundary(new("Shadows.NativeScene", true,
                graphics: draws.Select(draw => (RHIGraphicsPipelineRef)draw.Pipeline).ToArray(), operations: BoundaryOperations.RenderPasses)))
                boundary.Run(() =>
                {
                    foreach (var draw in draws) draw.Submit();
                });
            // Native storage and pose changes cannot turn partially drawn depth into
            // a current certificate. Recheck after the full restoration boundary too.
            selection.ValidateSubmission(draws, pages);
            CheckScene(changes, scene, completeInputsCurrent);
            pages.FinishReplacements(true, world, scene.Epoch, scene.Sequence, timestamp);
        }
        finally
        {
            // This slot was reserved before the first allocation. Any failure leaves
            // previous sampled tiles untouched and retires only unfinished replacements.
            if (pages.ReplacementsPending) pages.FinishReplacements(false);
        }
    }
    #endregion

    #region Private
    /// <summary>Requires an unchanged applied scene watermark and complete current native publications.</summary>
    private static void CheckScene(ShadowSceneChanges changes, ShadowChangeBatch expected, Func<bool> current)
    {
        var now = changes.Capture(expected.Sequence, expected.Epoch);
        if (!now.Complete || now.RequiresBroadInvalidation || now.Sequence != expected.Sequence ||
            now.Epoch != expected.Epoch || !current())
            throw new InvalidOperationException("Scene input changed during shadow replacement preparation.");
    }
    #endregion
}
