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
    /// <summary>Gets the canonical revision used by this derived layout, independently of owner installation revisions.</summary>
    internal ulong NativeMetadataRevision { get; private set; }

    /// <summary>Rebuilds derived tables from a manager-published snapshot without independently requerying native facts.</summary>
    internal void UseNativeMetadata(int program, GpuProgramMetadata data, ulong revision)
    {
        nativeMetadata = data;
        NativeMetadataRevision = revision;
        RebuildCache(program);
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
