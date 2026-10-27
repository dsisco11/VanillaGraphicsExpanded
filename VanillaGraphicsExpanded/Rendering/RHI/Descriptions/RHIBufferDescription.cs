using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Records legal creation intent, preserving omission independently of explicit zero values.</summary>
public struct RHIBufferDescription
{
    [Flags]
    private enum Inputs { Role = 1, Mode = 2, Capacity = 4, Usage = 8, Access = 16 }
    private Inputs assigned;
    private BufferRole role;
    private ResourceStorageMode mode;
    private nint capacity;
    private BufferUsage usage;
    private StorageAccess access;

    #region Public API
    /// <summary>Declares the owning abstraction's buffer role.</summary>
    public BufferRole Role { readonly get => role; set { role = value; assigned |= Inputs.Role; } }
    /// <summary>Requests realization, mutable storage or immutable storage.</summary>
    public ResourceStorageMode Mode { readonly get => mode; set { mode = value; assigned |= Inputs.Mode; } }
    /// <summary>Requests byte capacity, including explicit zero for mutable storage.</summary>
    public nint Capacity { readonly get => capacity; set { capacity = value; assigned |= Inputs.Capacity; } }
    /// <summary>Supplies the mutable allocation usage policy.</summary>
    public BufferUsage Usage { readonly get => usage; set { usage = value; assigned |= Inputs.Usage; } }
    /// <summary>Supplies immutable storage access, including an explicitly empty set.</summary>
    public StorageAccess Access { readonly get => access; set { access = value; assigned |= Inputs.Access; } }
    /// <summary>Provides an optional diagnostic label without defining resource identity.</summary>
    public string? Label { readonly get; set; }
    #endregion

    #region Internal API
    /// <summary>Rejects missing or contradictory inputs without inspecting a native resource.</summary>
    internal readonly void Validate()
    {
        if ((assigned & (Inputs.Role | Inputs.Mode)) != (Inputs.Role | Inputs.Mode)
            || !Enum.IsDefined(role) || !Enum.IsDefined(mode)) throw new ArgumentException("A valid role and storage mode are required.");
        if (mode == ResourceStorageMode.Realization)
        {
            if ((assigned & (Inputs.Capacity | Inputs.Usage | Inputs.Access)) != 0) throw new ArgumentException("Empty realization rejects allocation inputs.");
            return;
        }
        if ((assigned & Inputs.Capacity) == 0 || capacity < 0) throw new ArgumentException("An explicit nonnegative capacity is required.");
        if (mode == ResourceStorageMode.Mutable)
        {
            if ((assigned & Inputs.Usage) == 0 || !Enum.IsDefined(usage) || (assigned & Inputs.Access) != 0)
                throw new ArgumentException("Mutable storage requires usage and rejects immutable access flags.");
            return;
        }
        const StorageAccess all = StorageAccess.Read | StorageAccess.Write | StorageAccess.Persistent
            | StorageAccess.Coherent | StorageAccess.DynamicUpdates | StorageAccess.ClientStorage | StorageAccess.Sparse;
        if (access.HasFlag(StorageAccess.Sparse) && (access & (StorageAccess.Read | StorageAccess.Write)) != 0)
            throw new ArgumentException("Sparse storage cannot be mapped.");
        if (capacity == 0 || (assigned & Inputs.Access) == 0 || (assigned & Inputs.Usage) != 0 || (access & ~all) != 0
            || access.HasFlag(StorageAccess.Persistent) && (access & (StorageAccess.Read | StorageAccess.Write)) == 0
            || access.HasFlag(StorageAccess.Coherent) && !access.HasFlag(StorageAccess.Persistent))
            throw new ArgumentException("Invalid immutable storage request.");
    }
    #endregion
}
