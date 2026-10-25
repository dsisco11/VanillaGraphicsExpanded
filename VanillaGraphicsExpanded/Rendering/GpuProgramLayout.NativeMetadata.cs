using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Derives compatibility binding tables from canonical native program snapshots and authored preparation.</summary>
public partial class GpuProgramLayout
{
    private GpuProgramMetadata? nativeMetadata;

    #region Internal API
    /// <summary>Recovers only mutable legacy assignments when no registered owner observed the commands.</summary>
    internal void RefreshLocalProgramAssignments(GpuProgramObject owner)
    {
        if (compiledContract || uniformBlockContract.Count + shaderStorageBlockContract.Count + samplerContract.Count + imageContract.Count == 0) return;
        GpuResourceManager.RefreshLocalProgramAssignments(owner, uniformBlockContract.Keys, shaderStorageBlockContract.Keys,
            samplerContract.Keys.Concat(imageContract.Keys));
    }

    /// <summary>Gets the canonical revision used by this derived layout, independently of owner installation revisions.</summary>
    internal ulong NativeMetadataRevision { get; private set; }

    /// <summary>Rebuilds derived tables from a manager-published snapshot without independently requerying native facts.</summary>
    internal void UseNativeMetadata(int program, GpuProgramMetadata data, ulong revision)
    {
        if (nativeExecutableInvalidated)
            throw new InvalidOperationException("Native executable changes require a new prepared interface.");
        // Withdraw old prepared entries before validating replacements; failure cannot
        // leave fixed-slot resource submission using the previous executable's table.
        nativePreparationUnavailable = true;
        var prepared = binaryInterface?.RefreshResources(program, data);
        nativeMetadata = data;
        NativeMetadataRevision = revision;
        binaryInterface = prepared;
        nativePreparationUnavailable = false;
        RebuildCache(program);
    }

    /// <summary>Reconciles mutable resource assignments, or requests full preparation after executable replacement.</summary>
    internal void ObserveNativeMetadata(int program, GpuProgramMetadata data, ulong revision,
        GpuMetadataChange change, bool available)
    {
        if (binaryInterface is not null && (change & GpuMetadataChange.Executable) != 0)
            nativeExecutableInvalidated = true;
        if (!available || !data.ExecutableAvailable || nativeExecutableInvalidated)
        {
            // A changed executable also owns graphics stage/interface declarations. The
            // existing owner preparation path validates those together with resource slots.
            nativePreparationUnavailable = true;
            SetActiveSnapshot(null, null, null, null);
            return;
        }
        UseNativeMetadata(program, data, revision);
    }
    #endregion

    #region Private
    /// <summary>Projects named buffer bindings while retaining immutable metadata as the authority.</summary>
    private IReadOnlyDictionary<string, int> ProjectBufferBindings(ProgramInterface kind)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (nativeMetadata is not { ExecutableAvailable: true } data || data.Resources.IsDefault) return result;
        foreach (var resource in data.Resources)
            if (resource.Interface == kind && !string.IsNullOrWhiteSpace(resource.Name)
                && resource.Properties.TryGetValue(ProgramProperty.BufferBinding, out var values) && !values.IsDefaultOrEmpty)
                result[resource.Name] = values[0];
        return result;
    }

    /// <summary>Projects sampler and image units from canonical locations and type information.</summary>
    private (IReadOnlyDictionary<string, int> Samplers, IReadOnlyDictionary<string, int> Images) ProjectTextureUnits()
    {
        var samplers = new Dictionary<string, int>(StringComparer.Ordinal);
        var images = new Dictionary<string, int>(StringComparer.Ordinal);
        if (nativeMetadata is not { ExecutableAvailable: true } data || data.Resources.IsDefault || data.TextureUnits.IsDefault)
            return (samplers, images);
        var units = data.TextureUnits.ToDictionary(unit => unit.Location, unit => unit.Unit);
        foreach (var resource in data.Resources)
        {
            if (resource.Interface != ProgramInterface.Uniform || string.IsNullOrWhiteSpace(resource.Name)
                || !resource.Properties.TryGetValue(ProgramProperty.Type, out var types) || types.IsDefaultOrEmpty
                || !resource.Properties.TryGetValue(ProgramProperty.Location, out var locations) || locations.IsDefaultOrEmpty
                || !units.TryGetValue(locations[0], out int unit)) continue;
            var type = (ActiveUniformType)types[0];
            if (IsSamplerType(type)) samplers[NormalizeUniformName(resource.Name)] = unit;
            else if (IsImageType(type)) images[NormalizeUniformName(resource.Name)] = unit;
        }
        return (samplers, images);
    }
    #endregion
}
