using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Completes only unissued CPU requests; issued tickets retain independent GPU ownership.</summary>
internal interface IRHIRecordedResult
{
    #region Public API
    /// <summary>Withdraws an unexecuted request without touching enqueued work.</summary>
    void Abandon(Exception? error);
    #endregion
}
