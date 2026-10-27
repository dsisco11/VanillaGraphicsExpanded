namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Locates immutable typed arguments in one list's ordered command stream.</summary>
public readonly struct RHICommand
{
    #region Public API
    /// <summary>Gets the semantic operation, independent of native overloads.</summary>
    public CommandOperation Operation { get; }
    #endregion
    #region Internal API
    /// <summary>Gets the index within the operation's typed payload pages.</summary>
    internal int Payload { get; }
    /// <summary>Constructs a header after its complete arguments have been retained.</summary>
    internal RHICommand(CommandOperation operation, int payload) { Operation = operation; Payload = payload; }
    #endregion
}
