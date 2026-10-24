using System;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Captures executable interface facts independently of authored binding contracts and preparation policy.</summary>
internal sealed class GpuProgramMetadataReader : IGpuResourceMetadataReader<GpuProgramMetadata>
{
    internal static readonly GpuProgramMetadataReader Instance = new();

    #region Public API
    /// <inheritdoc />
    public bool IsResource(nint id) => id != 0 && id >= int.MinValue && id <= uint.MaxValue && GL.IsProgram(unchecked((int)id));

    /// <inheritdoc />
    public bool TryRead(nint id, out GpuProgramMetadata metadata)
    {
        metadata = default;
        if (!IsResource(id)) return false;
        int program = unchecked((int)id);
        GL.GetProgram(program, GetProgramParameterName.DeleteStatus, out int deleting);
        if (deleting != 0) return false;
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
        if (linked == 0)
        {
            metadata = new(false, ImmutableArray<GpuProgramResourceMetadata>.Empty, ImmutableArray<GpuProgramTextureUnitMetadata>.Empty);
            return true;
        }
        var resources = ImmutableArray.CreateBuilder<GpuProgramResourceMetadata>();
        var units = ImmutableArray.CreateBuilder<GpuProgramTextureUnitMetadata>();
        foreach (var kind in new[] { ProgramInterface.Uniform, ProgramInterface.UniformBlock, ProgramInterface.ShaderStorageBlock })
        {
            GL.GetProgramInterface(program, kind, ProgramInterfaceParameter.ActiveResources, out int count);
            GL.GetProgramInterface(program, kind, ProgramInterfaceParameter.MaxNameLength, out int capacity);
            if (count < 0 || capacity < 0) return false;
            var properties = kind == ProgramInterface.Uniform
                ? new[] { ProgramProperty.Type, ProgramProperty.Location, ProgramProperty.BlockIndex, ProgramProperty.ArraySize }
                : new[] { ProgramProperty.BufferBinding, ProgramProperty.BufferDataSize };
            for (int index = 0; index < count; index++)
            {
                var values = new int[properties.Length];
                GL.GetProgramResource(program, kind, index, properties.Length, properties, values.Length, out int written, values);
                if (written != values.Length) return false;
                GL.GetProgramResourceName(program, kind, index, Math.Max(1, capacity), out _, out string name);
                var facts = ImmutableDictionary.CreateBuilder<ProgramProperty, ImmutableArray<int>>();
                for (int property = 0; property < properties.Length; property++) facts.Add(properties[property], ImmutableArray.Create(values[property]));
                resources.Add(new(kind, index, name, facts.ToImmutable()));
                if (kind != ProgramInterface.Uniform || values[1] < 0 || values[2] != -1) continue;
                var type = (ActiveUniformType)values[0];
                if (!GpuProgramLayout.IsSamplerType(type) && !GpuProgramLayout.IsImageType(type)) continue;
                for (int element = 0; element < Math.Max(1, values[3]); element++)
                {
                    int location = values[1] + element;
                    GL.GetUniform(program, location, out int unit);
                    units.Add(new(location, unit));
                }
            }
        }
        metadata = new(true, resources.ToImmutable(), units.ToImmutable());
        return true;
    }
    #endregion
}
