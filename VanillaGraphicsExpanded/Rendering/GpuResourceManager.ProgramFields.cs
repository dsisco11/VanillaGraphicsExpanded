using System;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Constructs narrow executable-interface updates without reflecting unrelated resources.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Updates represented unit locations or a block binding while preserving the remaining interface.</summary>
    internal bool RefreshProgramFields(GpuResourceMetadataEntry<GpuProgramMetadata> entry, GpuProgramMetadata previous,
        bool unit, int location, int count, ProgramInterface block, int index, int? supplied)
    {
        RequireMetadataContext();
        RequireLiveEntry(entry);
        if (!CanReadProgramUnitMetadata(entry)) return false;
        BeginMetadataCompletion(entry, GpuMetadataChange.Metadata);
        bool finished = false;
        try
        {
            var candidate = previous;
            if (unit)
            {
                var units = previous.TextureUnits.ToBuilder();
                for (int i = 0; i < units.Count; i++)
                {
                    var current = units[i];
                    if (current.Location < location || (long)current.Location - location >= count) continue;
                    int value;
                    if (supplied.HasValue) value = supplied.Value;
                    else value = RHI.RHICommandContext.Current.ReadExternalProgramUnit((int)entry.Id, current.Location);
                    units[i] = current with { Unit = value };
                }
                candidate = candidate with { TextureUnits = units.ToImmutable() };
            }
            else
            {
                var resources = previous.Resources.ToBuilder();
                for (int i = 0; i < resources.Count; i++)
                {
                    var resource = resources[i];
                    if (resource.Interface != block || resource.Index != index) continue;
                    int value;
                    if (supplied.HasValue) value = supplied.Value;
                    else
                    {
                        if (!RHI.RHICommandContext.Current.ReadExternalProgramBlockBinding((int)entry.Id, block, index, out value)) return false;
                    }
                    resources[i] = resource with { Properties = resource.Properties.SetItem(ProgramProperty.BufferBinding, ImmutableArray.Create(value)) };
                }
                candidate = candidate with { Resources = resources.ToImmutable() };
            }
            RequireMetadataContext();
            RequireLiveEntry(entry);
            RequireUninterruptedCompletion(entry);
            entry.Publish(candidate, GpuMetadataChange.Metadata | entry.PendingChange);
            finished = true;
            return true;
        }
        finally
        {
            entry.CompletionInProgress = false;
            entry.NotifyProgramDependents(GpuMetadataChange.Metadata, finished);
        }
    }
    #endregion
}
