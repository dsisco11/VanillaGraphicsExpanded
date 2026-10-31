using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Retains original imported array/context and change-delivered readiness without polling configuration metadata.</summary>
internal sealed class RHIGeometryImport
{
    private bool withdrawn;

    #region Internal API
    /// <summary>Gets a nonowning recipient for an already configured native array.</summary>
    internal GpuResource<GpuVertexArrayMetadata> Array { get; }
    /// <summary>Reports change-delivered certification readiness without resolving resource metadata.</summary>
    internal bool IsPrepared => !withdrawn;
    /// <summary>Notifies the integration owner when its original array certification is withdrawn.</summary>
    internal event Action? Withdrawn;
    /// <summary>Gets the importer-established originating native namespace.</summary>
    internal (nint Handle, long Generation) Origin { get; }
    /// <summary>Captures only completed certified identity; this never queries or recovers external configuration.</summary>
    internal RHIGeometryImport(GpuResource<GpuVertexArrayMetadata> array, (nint Handle, long Generation) origin)
    {
        Array = array;
        Origin = origin;
        array.ObservePreparation(this, Withdraw);
    }
    /// <summary>Checks change-delivered certification rather than repeatedly resolving the original array.</summary>
    internal void RequirePrepared((nint Handle, long Generation) origin)
    {
        if (withdrawn || Origin != origin) throw new InvalidOperationException("Imported geometry certification was withdrawn.");
    }
    /// <summary>Withdraws the exact import; a changed original array requires explicit producer recertification.</summary>
    internal void Withdraw()
    {
        if (withdrawn) return;
        // Close readiness before asking the owning integration to schedule a replacement.
        withdrawn = true;
        Withdrawn?.Invoke();
    }
    #endregion
}
