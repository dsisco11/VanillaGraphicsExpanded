using System.Runtime.CompilerServices;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains unsupported unit-representation availability on the original program registration.</summary>
internal sealed partial class GpuResourceManager
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
            if (resource.Interface != OpenTK.Graphics.OpenGL.ProgramInterface.Uniform
                || !resource.Properties.TryGetValue(OpenTK.Graphics.OpenGL.ProgramProperty.Location, out var locations) || locations.IsDefaultOrEmpty
                || !resource.Properties.TryGetValue(OpenTK.Graphics.OpenGL.ProgramProperty.Type, out var types) || types.IsDefaultOrEmpty) continue;
            int count = resource.Properties.TryGetValue(OpenTK.Graphics.OpenGL.ProgramProperty.ArraySize, out var sizes) && !sizes.IsDefaultOrEmpty ? sizes[0] : 1;
            if (locations[0] < 0 || location < locations[0] || (long)location - locations[0] >= count) continue;
            var type = (OpenTK.Graphics.OpenGL.ActiveUniformType)types[0];
            int limit = GpuProgramLayout.IsImageType(type) ? GpuSupport.Graphics.MaxImageUnits
                : GpuProgramLayout.IsSamplerType(type) ? GpuSupport.Graphics.MaxCombinedTextureImageUnits : -1;
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
    #endregion

    #region Private
    /// <summary>Prevents ordinary integer readback from certifying an unsupported handle as a texture unit.</summary>
    private bool CanReadProgramUnitMetadata(GpuResourceRegistration entry) =>
        entry.Kind != GpuResourceKind.Program || !unsupportedProgramUnits.TryGetValue(entry, out _);
    #endregion
}
