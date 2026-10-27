namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Describes exclusive CPU recording and single-use dispatch, independently of GPU completion.</summary>
public enum CommandListState
{
    /// <summary>Accepts supplied CPU arguments.</summary>
    Recording,
    /// <summary>Publishes frozen commands for submission.</summary>
    Closed,
    /// <summary>Owns synchronous dispatch; cancellation and reset are forbidden.</summary>
    Executing,
    /// <summary>Completed CPU dispatch without implying GPU completion.</summary>
    Executed,
    /// <summary>Stopped at the first failed operation.</summary>
    Failed,
    /// <summary>Released unexecuted CPU work without native effects.</summary>
    Cancelled
}
