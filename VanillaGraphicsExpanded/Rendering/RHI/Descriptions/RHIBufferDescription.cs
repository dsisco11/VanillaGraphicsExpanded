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
    /// <summary>Preserves assignment intent while lowering to the single family validation boundary.</summary>
    internal readonly GpuBufferInitialization ToInitialization()
    {
        if ((assigned & (Inputs.Role | Inputs.Mode)) != (Inputs.Role | Inputs.Mode)
            || !Enum.IsDefined(role) || !Enum.IsDefined(mode)) throw new ArgumentException("A valid role and storage mode are required.");
        // Copy contradictory assignments too: lowering must not silently erase inputs
        // that family preparation needs to reject before native allocation.
        var result = new GpuBufferInitialization { Mode = mode switch
        {
            ResourceStorageMode.Realization => GpuBufferStorageMode.Realization,
            ResourceStorageMode.Mutable => GpuBufferStorageMode.Mutable,
            ResourceStorageMode.Immutable => GpuBufferStorageMode.Immutable,
            _ => throw new ArgumentOutOfRangeException(nameof(Mode))
        } };
        if ((assigned & Inputs.Capacity) != 0) result.Capacity = capacity;
        if ((assigned & Inputs.Usage) != 0) result.Usage = usage;
        if ((assigned & Inputs.Access) != 0) result.StorageFlags = access;
        return result;
    }
    #endregion
}
