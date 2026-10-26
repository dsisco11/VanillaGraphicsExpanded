using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Identifies the intended consumer of buffer storage independently of native binding targets.</summary>
public enum RHIBufferRole { Vertex, Index, Uniform, ShaderStorage, AtomicCounter, IndirectDraw, IndirectDispatch, PixelPack, PixelUnpack, CopySource, CopyDestination, TransformFeedback }

/// <summary>Distinguishes a realized empty object from mutable and immutable storage.</summary>
public enum RHIStorageMode { Realization, Mutable, Immutable }

/// <summary>Expresses update frequency and intended producer/consumer access.</summary>
public enum RHIBufferUsage { StaticDraw, StaticRead, StaticCopy, DynamicDraw, DynamicRead, DynamicCopy, StreamDraw, StreamRead, StreamCopy }

/// <summary>Declares immutable allocation access without exposing native flags.</summary>
[Flags]
public enum RHIStorageAccess { None = 0, Read = 1, Write = 2, Persistent = 4, Coherent = 8, DynamicUpdates = 16, ClientStorage = 32 }

/// <summary>Declares the CPU access and invalidation contract of an immediate mapping.</summary>
[Flags]
public enum RHIBufferMapAccess { None = 0, Read = 1, Write = 2, InvalidateRange = 4, InvalidateBuffer = 8, ExplicitFlush = 16, Unsynchronized = 32, Persistent = 64, Coherent = 128 }

/// <summary>Records legal creation intent, preserving omission independently of explicit zero values.</summary>
public struct RHIBufferDescription
{
    [Flags]
    private enum Inputs { Role = 1, Mode = 2, Capacity = 4, Usage = 8, Access = 16 }
    private Inputs assigned;
    private RHIBufferRole role;
    private RHIStorageMode mode;
    private nint capacity;
    private RHIBufferUsage usage;
    private RHIStorageAccess access;

    #region Public API
    /// <summary>Declares the owning abstraction's buffer role.</summary>
    public RHIBufferRole Role { readonly get => role; set { role = value; assigned |= Inputs.Role; } }
    /// <summary>Requests realization, mutable storage or immutable storage.</summary>
    public RHIStorageMode Mode { readonly get => mode; set { mode = value; assigned |= Inputs.Mode; } }
    /// <summary>Requests byte capacity, including explicit zero for mutable storage.</summary>
    public nint Capacity { readonly get => capacity; set { capacity = value; assigned |= Inputs.Capacity; } }
    /// <summary>Supplies the mutable allocation usage policy.</summary>
    public RHIBufferUsage Usage { readonly get => usage; set { usage = value; assigned |= Inputs.Usage; } }
    /// <summary>Supplies immutable storage access, including an explicitly empty set.</summary>
    public RHIStorageAccess Access { readonly get => access; set { access = value; assigned |= Inputs.Access; } }
    /// <summary>Provides an optional diagnostic label without defining resource identity.</summary>
    public string? Label { readonly get; set; }
    #endregion

    #region Internal API
    /// <summary>Rejects missing or contradictory inputs without inspecting a native resource.</summary>
    internal readonly void Validate()
    {
        if ((assigned & (Inputs.Role | Inputs.Mode)) != (Inputs.Role | Inputs.Mode)
            || !Enum.IsDefined(role) || !Enum.IsDefined(mode)) throw new ArgumentException("A valid role and storage mode are required.");
        if (mode == RHIStorageMode.Realization)
        {
            if ((assigned & (Inputs.Capacity | Inputs.Usage | Inputs.Access)) != 0) throw new ArgumentException("Empty realization rejects allocation inputs.");
            return;
        }
        if ((assigned & Inputs.Capacity) == 0 || capacity < 0) throw new ArgumentException("An explicit nonnegative capacity is required.");
        if (mode == RHIStorageMode.Mutable)
        {
            if ((assigned & Inputs.Usage) == 0 || !Enum.IsDefined(usage) || (assigned & Inputs.Access) != 0)
                throw new ArgumentException("Mutable storage requires usage and rejects immutable access flags.");
            return;
        }
        const RHIStorageAccess all = RHIStorageAccess.Read | RHIStorageAccess.Write | RHIStorageAccess.Persistent
            | RHIStorageAccess.Coherent | RHIStorageAccess.DynamicUpdates | RHIStorageAccess.ClientStorage;
        if (capacity == 0 || (assigned & Inputs.Access) == 0 || (assigned & Inputs.Usage) != 0 || (access & ~all) != 0
            || access.HasFlag(RHIStorageAccess.Persistent) && (access & (RHIStorageAccess.Read | RHIStorageAccess.Write)) == 0
            || access.HasFlag(RHIStorageAccess.Coherent) && !access.HasFlag(RHIStorageAccess.Persistent))
            throw new ArgumentException("Invalid immutable storage request.");
    }
    #endregion
}
