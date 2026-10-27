using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Declares immutable allocation access without exposing native flags.</summary>
[Flags]
public enum StorageAccess { None = 0, Read = 1, Write = 2, Persistent = 4, Coherent = 8, DynamicUpdates = 16, ClientStorage = 32, Sparse = 64 }
