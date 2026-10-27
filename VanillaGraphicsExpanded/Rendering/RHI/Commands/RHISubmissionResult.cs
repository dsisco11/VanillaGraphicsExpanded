using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Reports synchronous dispatch and the original failure without claiming GPU completion.</summary>
public readonly record struct RHISubmissionResult(CommandListState State, int FailureIndex = -1, Exception? Error = null, Exception? CleanupError = null);
