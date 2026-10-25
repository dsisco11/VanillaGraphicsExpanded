using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Transfers executable facts between owners without reflecting the same interface again.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Requires the source owner's available executable snapshot before ownership transfer.</summary>
    internal static GpuProgramMetadata RequireProgramSnapshot(GpuProgramObject source)
    {
        ObjectDisposedException.ThrowIf(!source.IsValid, source);
        StateCache.Current.RequireResourceOperationAvailable(GpuResourceKind.Program, source.ProgramId);
        if (source.ResourceManager?.GetResourceRegistration(source) is { } entry
            && !entry.Manager.IsResourceMetadataAvailable(source))
            throw new InvalidOperationException("The original program metadata is unavailable.");
        var data = source.MetadataSnapshot;
        if (!data.ExecutableAvailable) throw new InvalidOperationException("A linked executable is required for transfer.");
        return data;
    }

    /// <summary>Associates a new owner with existing facts, or delivers the supplied context-local result.</summary>
    internal static void InstallTransferredProgramMetadata(GpuResource<GpuProgramMetadata> recipient, GpuProgramMetadata data)
    {
        StateCache.Current.RequireResourceOperationAvailable(GpuResourceKind.Program, recipient.ResourceId);
        if (recipient.ResourceManager is { } manager && manager.CanAcquireWrapperMetadata(recipient))
        {
            var entry = manager.FindResource(GpuResourceKind.Program, recipient.ResourceId) as GpuResourceMetadataEntry<GpuProgramMetadata>;
            if (entry is not { IsUsable: true }) throw new InvalidOperationException("Transferred program metadata is unavailable.");
            manager.AssociateResource(entry, recipient);
        }
        else recipient.HandleMetadataChanged(data);
    }

    /// <summary>Recovers only legacy-authored mutable fields when commands had no registered observation owner.</summary>
    internal static void RefreshLocalProgramAssignments(GpuProgramObject owner, IEnumerable<string> uniformBlocks,
        IEnumerable<string> storageBlocks, IEnumerable<string> units)
    {
        if (owner.ResourceManager?.GetResourceRegistration(owner) is not null) return;
        var previous = RequireProgramSnapshot(owner);
        var blocks = new HashSet<string>(uniformBlocks, StringComparer.Ordinal);
        var storage = new HashSet<string>(storageBlocks, StringComparer.Ordinal);
        var uniforms = new HashSet<string>(units, StringComparer.Ordinal);
        using var operation = new MetadataOperation<GpuProgramMetadata>(owner, GpuMetadataChange.Metadata);
        var resources = previous.Resources.ToBuilder();
        var textureUnits = previous.TextureUnits.ToBuilder();
        // The executable's existing interface supplies locations and indices. Only values
        // which the legacy authored contract may have changed need native recovery.
        for (int i = 0; i < resources.Count; i++)
        {
            var resource = resources[i];
            if (resource.Interface == ProgramInterface.UniformBlock && blocks.Contains(resource.Name)
                || resource.Interface == ProgramInterface.ShaderStorageBlock && storage.Contains(resource.Name))
            {
                int[] values = [-1];
                GL.GetProgramResource(owner.ProgramId, resource.Interface, resource.Index, 1, [ProgramProperty.BufferBinding], 1, out int written, values);
                if (written != 1 || values[0] < 0) throw new InvalidOperationException("Program binding recovery failed.");
                resources[i] = resource with { Properties = resource.Properties.SetItem(ProgramProperty.BufferBinding, ImmutableArray.Create(values[0])) };
            }
            else if (resource.Interface == ProgramInterface.Uniform && uniforms.Contains(resource.Name?.EndsWith("[0]", StringComparison.Ordinal) == true ? resource.Name[..^3] : resource.Name ?? string.Empty)
                && resource.Properties.TryGetValue(ProgramProperty.Location, out var locations) && !locations.IsDefaultOrEmpty)
            {
                for (int j = 0; j < textureUnits.Count; j++)
                    if (textureUnits[j].Location == locations[0])
                    {
                        GL.GetUniform(owner.ProgramId, locations[0], out int value);
                        textureUnits[j] = textureUnits[j] with { Unit = value };
                    }
            }
        }
        operation.Commit(previous with { Resources = resources.ToImmutable(), TextureUnits = textureUnits.ToImmutable() });
        owner.OnNativeProgramMetadataChanged(GpuMetadataChange.Metadata, true);
    }
    #endregion
}
