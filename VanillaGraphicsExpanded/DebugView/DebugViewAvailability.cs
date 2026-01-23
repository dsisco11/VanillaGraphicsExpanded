namespace VanillaGraphicsExpanded.DebugView;

public readonly record struct DebugViewAvailability(bool IsAvailable, string? Reason = null)
{
    public static DebugViewAvailability Available() => new(true);

    public static DebugViewAvailability Unavailable(string reason) => new(false, reason);
}

