using System;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes supplied executable assignments without rereading or reflecting the interface.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Patches represented locations from a validated call against the previously available executable.</summary>
    internal bool PublishProgramAssignment(GpuResourceMetadataEntry<GpuProgramMetadata> entry, GpuProgramMetadata previous,
        bool unit, int location, ReadOnlySpan<int> values, ProgramBlockKind block, int index)
    {
        RequireMetadataContext();
        RequireLiveEntry(entry);
        if (!CanReadProgramUnitMetadata(entry) || !previous.ExecutableAvailable
            || previous.Resources.IsDefault || previous.TextureUnits.IsDefault) return false;
        var candidate = previous;
        if (unit)
        {
            var units = previous.TextureUnits.ToBuilder();
            for (int i = 0; i < units.Count; i++)
            {
                long offset = (long)units[i].Location - location;
                if (offset >= 0 && offset < values.Length)
                    units[i] = units[i] with { Unit = values[(int)offset] };
            }
            candidate = candidate with { TextureUnits = units.ToImmutable() };
        }
        else
        {
            var resources = previous.Resources.ToBuilder();
            for (int i = 0; i < resources.Count; i++)
                if (resources[i].Interface == (block == ProgramBlockKind.Uniform ? ProgramResourceKind.UniformBlock : ProgramResourceKind.ShaderStorageBlock)
                    && resources[i].Index == index)
                    resources[i] = resources[i] with { BufferBinding = values[0] };
            candidate = candidate with { Resources = resources.ToImmutable() };
        }
        // Assignment arithmetic has no callback boundary. Commit before notifying dependent
        // preparation; publication failure cannot roll back the already executed native change.
        entry.Publish(candidate, GpuMetadataChange.Metadata | entry.PendingChange);
        entry.NotifyProgramDependents(GpuMetadataChange.Metadata, true);
        return true;
    }
    #endregion
}
