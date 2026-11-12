using System;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Pages;

/// <summary>Completes or abandons an entire bounded replacement interval without assignment readback.</summary>
internal sealed partial class ShadowPagePool
{
    #region Public API
    /// <summary>Publishes frozen depths only after every required draw and input recheck has succeeded.</summary>
    /// <remarks>Failure retires only this interval's unfinished assignments. The final table pass is reserved
    /// before allocation and bounded by configured table capacity and probe limit.</remarks>
    internal void FinishReplacements(bool complete, long worldRevision = 0, long sceneEpoch = 0,
        long sceneSequence = 0, double timestamp = 0)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!CanSubmit || !completionReserved)
            throw new InvalidOperationException("No replacement interval is available to finish.");
        if (complete && (!writesFrozen || worldRevision <= 0 || sceneEpoch <= 0 || sceneSequence < 0 ||
            !double.IsFinite(timestamp) || timestamp < 0))
            throw new ArgumentException("Successful completion requires frozen writes and current scene dependencies.");
        var request = new ShadowPageRequest(ShadowPageOperation.FinishReplacements, default, default,
            worldRevision, sceneEpoch, sceneSequence, Complete: complete, Timestamp: timestamp);
        operations++;
        Dispatch(request.Pack());
        completionReserved = false;
    }
    #endregion
}
