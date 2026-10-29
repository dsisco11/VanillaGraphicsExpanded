using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains unsupported unit-representation availability on the original program registration.</summary>
internal sealed partial class RHIResourceManager
{
    private readonly ConditionalWeakTable<GpuResourceRegistration, object> unsupportedProgramUnits = new();

    #region Internal API
    /// <summary>Validates a controlled unit assignment using already available interface facts and cached limits.</summary>
    internal void ValidateProgramUnit(int program, int location, int unit)
    {
        if (FindObservedResource(GpuResourceKind.Program, program) is not GpuResourceMetadataEntry<GpuProgramMetadata> entry
            || !entry.IsUsable || entry.Metadata.Resources.IsDefault) return;
        foreach (var resource in entry.Metadata.Resources)
        {
            if (resource.Interface != ProgramResourceKind.Uniform
                || resource.Location is not { } start || resource.Type is not { } type
                || resource.ArraySize is not { } count) continue;
            if (start < 0 || location < start || (long)location - start >= count) continue;
            int limit = GpuProgramLayout.IsImageType(type) ? RHI.RHICommandContext.Current.Capabilities.MaxImageUnits
                : GpuProgramLayout.IsSamplerType(type) ? RHI.RHICommandContext.Current.Capabilities.MaxCombinedTextureImageUnits : -1;
            if (limit >= 0 && (uint)unit >= (uint)limit) throw new System.ArgumentOutOfRangeException(nameof(unit));
            return;
        }
    }

    /// <summary>Certifies an unchanged represented unit from available canonical facts without another native read.</summary>
    internal bool IsProgramUnitCurrent(int program, int location, int unit)
    {
        if (FindObservedResource(GpuResourceKind.Program, program) is not GpuResourceMetadataEntry<GpuProgramMetadata> entry
            || !entry.IsUsable || entry.Metadata.TextureUnits.IsDefault) return false;
        foreach (var assignment in entry.Metadata.TextureUnits)
            if (assignment.Location == location) return assignment.Unit == unit;
        return false;
    }

    /// <summary>Withdraws unit snapshots when an observed command can switch a represented uniform to an opaque handle.</summary>
    internal void WithdrawUnsupportedProgramUnits(GpuResourceMetadataEntry<GpuProgramMetadata> entry)
    {
        RequireMetadataContext();
        RequireLiveEntry(entry);
        unsupportedProgramUnits.GetValue(entry, static _ => new object());
    }
    /// <summary>Clears unsupported unit history only for a replacement executable output.</summary>
    internal void ResetProgramUnitRepresentation(GpuResourceRegistration entry) => unsupportedProgramUnits.Remove(entry);
    #endregion

    #region Private
    /// <summary>Prevents ordinary integer readback from certifying an unsupported handle as a texture unit.</summary>
    private bool CanReadProgramUnitMetadata(GpuResourceRegistration entry) =>
        entry.Kind != GpuResourceKind.Program || !unsupportedProgramUnits.TryGetValue(entry, out _);
    #endregion
}
