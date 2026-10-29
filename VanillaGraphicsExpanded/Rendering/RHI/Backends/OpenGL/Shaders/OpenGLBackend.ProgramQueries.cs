using System;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Captures executable interface facts independently of authored binding contracts and preparation policy.</summary>
internal sealed partial class OpenGLBackend
{

    #region Internal API

    /// <summary>Reads canonical executable output facts once for manager-owned acquisition or publication.</summary>
    public bool ReadProgramMetadata(nint id, out GpuProgramMetadata metadata)
    {
        RequireContext();
        metadata = default;
        if (id == 0 || id < int.MinValue || id > uint.MaxValue) return false;
        int program = unchecked((int)id);
        // Descriptor queries establish availability directly; tracked refresh needs no
        // separate glIsProgram query and leaves the caller's pending errors untouched.
        int deleting = -1, linked = -1;
        OpenGLDriverCalls.GetProgram(program, Native.GetProgramParameterName.DeleteStatus, out deleting);
        RequireContext();
        if (deleting != 0) return false;
        OpenGLDriverCalls.GetProgram(program, Native.GetProgramParameterName.LinkStatus, out linked);
        RequireContext();
        if (linked < 0) return false;
        if (linked == 0)
        {
            metadata = new(false, ImmutableArray<GpuProgramResourceMetadata>.Empty, ImmutableArray<GpuProgramTextureUnitMetadata>.Empty);
            return true;
        }
        var resources = ImmutableArray.CreateBuilder<GpuProgramResourceMetadata>();
        var units = ImmutableArray.CreateBuilder<GpuProgramTextureUnitMetadata>();
        foreach (var kind in new[] { ProgramInterface.Uniform, ProgramInterface.UniformBlock,
            ProgramInterface.ShaderStorageBlock, ProgramInterface.AtomicCounterBuffer,
            ProgramInterface.ProgramInput, ProgramInterface.ProgramOutput })
        {
            int count = -1, capacity = -1;
            GL.GetProgramInterface(program, (Native.ProgramInterface)kind, Native.ProgramInterfaceParameter.ActiveResources, out count);
            RequireContext();
            if (kind == ProgramInterface.AtomicCounterBuffer) capacity = 0;
            else GL.GetProgramInterface(program, (Native.ProgramInterface)kind, Native.ProgramInterfaceParameter.MaxNameLength, out capacity);
            RequireContext();
            if (count < 0 || capacity < 0) return false;
            var properties = kind is ProgramInterface.ProgramInput or ProgramInterface.ProgramOutput
                ? kind == ProgramInterface.ProgramOutput
                    ? new[] { ProgramProperty.Location, ProgramProperty.Type, ProgramProperty.ArraySize, ProgramProperty.LocationIndex }
                    : new[] { ProgramProperty.Location, ProgramProperty.Type, ProgramProperty.ArraySize }
                : kind == ProgramInterface.Uniform
                ? new[] { ProgramProperty.Type, ProgramProperty.Location, ProgramProperty.BlockIndex, ProgramProperty.ArraySize }
                : kind == ProgramInterface.AtomicCounterBuffer
                    ? new[] { ProgramProperty.BufferBinding, ProgramProperty.BufferDataSize }
                    : new[] { ProgramProperty.BufferBinding, ProgramProperty.BufferDataSize,
                        ProgramProperty.ReferencedByVertexShader, ProgramProperty.ReferencedByTessControlShader,
                        ProgramProperty.ReferencedByTessEvaluationShader, ProgramProperty.ReferencedByGeometryShader,
                        ProgramProperty.ReferencedByFragmentShader, (ProgramProperty)All.ReferencedByComputeShader };
            var nativeProperties = System.Array.ConvertAll(properties, value => (Native.ProgramProperty)value);
            for (int index = 0; index < count; index++)
            {
                var values = new int[properties.Length];
                GL.GetProgramResource(program, (Native.ProgramInterface)kind, index, nativeProperties.Length, nativeProperties, values.Length, out int written, values);
                RequireContext();
                if (written != values.Length) return false;
                string name = string.Empty;
                // Atomic-counter buffers are indexed resources without queryable names.
                if (kind != ProgramInterface.AtomicCounterBuffer)
                {
                    GL.GetProgramResourceName(program, (Native.ProgramInterface)kind, index, Math.Max(1, capacity), out _, out name);
                    RequireContext();
                }
                // Only fields valid for this category are present; omitted facts stay unavailable.
                int? Fact(ProgramProperty property)
                {
                    int at = Array.IndexOf(properties, property);
                    return at < 0 ? null : values[at];
                }
                ShaderStageMask? stages = null;
                if (kind is ProgramInterface.UniformBlock or ProgramInterface.ShaderStorageBlock)
                {
                    stages = ShaderStageMask.None;
                    if (Fact(ProgramProperty.ReferencedByVertexShader) != 0) stages |= ShaderStageMask.Vertex;
                    if (Fact(ProgramProperty.ReferencedByTessControlShader) != 0) stages |= ShaderStageMask.TessellationControl;
                    if (Fact(ProgramProperty.ReferencedByTessEvaluationShader) != 0) stages |= ShaderStageMask.TessellationEvaluation;
                    if (Fact(ProgramProperty.ReferencedByGeometryShader) != 0) stages |= ShaderStageMask.Geometry;
                    if (Fact(ProgramProperty.ReferencedByFragmentShader) != 0) stages |= ShaderStageMask.Fragment;
                    if (Fact((ProgramProperty)All.ReferencedByComputeShader) != 0) stages |= ShaderStageMask.Compute;
                }
                var type = Fact(ProgramProperty.Type) is { } token
                    ? OpenGLShaderValues.Value((Native.All)token) : (ShaderValueType?)null;
                resources.Add(new(OpenGLShaderValues.Resource((Native.ProgramInterface)kind), index, name,
                    type, Fact(ProgramProperty.Location), Fact(ProgramProperty.ArraySize), Fact(ProgramProperty.BlockIndex),
                    Fact(ProgramProperty.BufferBinding), Fact(ProgramProperty.BufferDataSize), stages, Fact(ProgramProperty.LocationIndex)));
                if (kind != ProgramInterface.Uniform || values[1] < 0 || values[2] != -1 || type is null) continue;
                if (!GpuProgramLayout.IsSamplerType(type.Value) && !GpuProgramLayout.IsImageType(type.Value)) continue;
                for (int element = 0; element < Math.Max(1, values[3]); element++)
                {
                    int location = values[1] + element;
                    GL.GetUniform(program, location, out int unit);
                    RequireContext();
                    units.Add(new(location, unit));
                }
            }
        }
        metadata = new(true, resources.ToImmutable(), units.ToImmutable());
        return true;
    }
    #endregion
}
