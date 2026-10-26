namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Explicit platform-handle import intent; this value never closes or duplicates the OS handle.</summary>
public readonly record struct RHIExternalHandle(RHIExternalHandleKind Kind, nint Value, RHIExternalHandleOwnership Ownership);
