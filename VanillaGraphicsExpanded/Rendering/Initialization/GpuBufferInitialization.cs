using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Distinguishes realization from mutable and immutable allocation requests.</summary>
internal enum GpuBufferStorageMode { Realization, Mutable, Immutable }

/// <summary>Retains legal buffer initialization inputs and automatically records assignment intent.</summary>
internal struct GpuBufferInitialization
{
    private Inputs assigned;
    private GpuBufferStorageMode mode;
    private nint capacity;
    private BufferUsageHint usage;
    private BufferStorageFlags flags;

    /// <summary>Tracks intent independently of zero-valued input properties.</summary>
    [Flags]
    private enum Inputs { Mode = 1, Capacity = 2, Usage = 4, Flags = 8 }

    #region Public API
    /// <summary>Selects realization-only or an explicit allocation operation; assignment is required.</summary>
    public GpuBufferStorageMode Mode { readonly get => mode; set { mode = value; assigned |= Inputs.Mode; } }
    /// <summary>Requests native-width byte capacity; zero is valid for mutable allocation.</summary>
    public nint Capacity { readonly get => capacity; set { capacity = value; assigned |= Inputs.Capacity; } }
    /// <summary>Requests mutable usage, independently of the zero value of the enumeration.</summary>
    public BufferUsageHint Usage { readonly get => usage; set { usage = value; assigned |= Inputs.Usage; } }
    /// <summary>Requests immutable storage flags, including an explicitly empty set.</summary>
    public BufferStorageFlags StorageFlags { readonly get => flags; set { flags = value; assigned |= Inputs.Flags; } }
    #endregion

    #region Internal API
    /// <summary>Validates assigned inputs without querying a native object for omitted allocation values.</summary>
    internal readonly void Validate()
    {
        if ((assigned & Inputs.Mode) == 0 || !Enum.IsDefined(mode))
            throw new ArgumentException("An explicit buffer storage mode is required.");
        if (mode == GpuBufferStorageMode.Realization)
        {
            if (assigned != Inputs.Mode) throw new ArgumentException("Realization does not accept storage inputs.");
            return;
        }
        if ((assigned & Inputs.Capacity) == 0 || capacity < 0)
            throw new ArgumentException("An explicit nonnegative buffer capacity is required.");
        if (mode == GpuBufferStorageMode.Mutable)
        {
            if ((assigned & Inputs.Usage) == 0 || !Enum.IsDefined(usage) || (assigned & Inputs.Flags) != 0)
                throw new ArgumentException("Mutable allocation requires usage and rejects storage flags.");
            return;
        }
        const BufferStorageFlags supported = BufferStorageFlags.DynamicStorageBit | BufferStorageFlags.ClientStorageBit
            | BufferStorageFlags.MapReadBit | BufferStorageFlags.MapWriteBit | BufferStorageFlags.MapPersistentBit
            | BufferStorageFlags.MapCoherentBit;
        if (capacity == 0 || (assigned & Inputs.Flags) == 0 || (assigned & Inputs.Usage) != 0 || (flags & ~supported) != 0)
            throw new ArgumentException("Immutable allocation requires positive capacity and explicit supported storage flags.");
        if ((flags & BufferStorageFlags.MapPersistentBit) != 0
            && (flags & (BufferStorageFlags.MapReadBit | BufferStorageFlags.MapWriteBit)) == 0
            || (flags & BufferStorageFlags.MapCoherentBit) != 0 && (flags & BufferStorageFlags.MapPersistentBit) == 0)
            throw new ArgumentException("Persistent mapping requires access; coherent mapping requires persistence.");
    }
    #endregion
}
