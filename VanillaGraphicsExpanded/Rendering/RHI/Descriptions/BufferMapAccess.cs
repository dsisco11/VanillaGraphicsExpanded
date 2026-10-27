using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Declares the CPU access and invalidation contract of an immediate mapping.</summary>
[Flags]
public enum BufferMapAccess { None = 0, Read = 1, Write = 2, InvalidateRange = 4, InvalidateBuffer = 8, ExplicitFlush = 16, Unsynchronized = 32, Persistent = 64, Coherent = 128 }
