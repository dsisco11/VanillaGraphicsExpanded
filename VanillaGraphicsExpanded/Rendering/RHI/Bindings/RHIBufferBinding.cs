namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Retains an authored uniform/storage/counter buffer input with explicit whole or ranged selection.</summary>
public readonly record struct RHIBufferBinding(ulong Identity, BufferRole Role, GpuBufferRef Buffer,
    nint Offset = 0, nint Length = 0, bool Whole = true);
