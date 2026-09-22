using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Spirv;

/// <summary>Owns the active numeric interface of one linked program; slots are fixed by its compiled contract.</summary>
internal sealed class GpuProgramInterface
{
    private readonly int program;
    private readonly Resources resources;
    /// <summary>Active locations and diagnostic resource indices belonging to this program.</summary>
    private sealed record Resources(Dictionary<string, int> Uniforms, Dictionary<string, int> Blocks, Dictionary<string, int> Storage, Dictionary<int, int> ArraySizes);

    #region Registration
    /// <summary>Matches active locations and initial block bindings without querying any SPIR-V debug name.</summary>
    public GpuProgramInterface(int program, IEnumerable<GpuBindingContract> contracts)
    {
        this.program = program;
        var array = contracts.ToArray();
        var active = new HashSet<int>();
        var arraySizes = new Dictionary<int, int>();
        GL.GetProgramInterface(program, ProgramInterface.Uniform, ProgramInterfaceParameter.ActiveResources, out int count);
        for (int i = 0; i < count; i++)
        {
            int[] values = new int[2];
            GL.GetProgramResource(program, ProgramInterface.Uniform, i, 2, [ProgramProperty.Location, ProgramProperty.ArraySize], 2, out _, values);
            if (values[0] >= 0)
            {
                arraySizes[values[0]] = Math.Max(1, values[1]);
                for (int j = 0; j < arraySizes[values[0]]; j++) active.Add(values[0] + j);
            }
        }
        var uniforms = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in array.SelectMany(v => v.UniformLocations))
            uniforms[pair.Key] = active.Contains(pair.Value) ? pair.Value : -1;
        resources = new(uniforms, Blocks(program, ProgramInterface.UniformBlock, array.SelectMany(v => v.UniformBlocks).Select(p => new KeyValuePair<string, int>(p.Key, p.Value.Slot))),
            Blocks(program, ProgramInterface.ShaderStorageBlock, array.SelectMany(v => v.StorageBlocks).Select(p => new KeyValuePair<string, int>(p.Key, p.Value.Slot))), arraySizes);
    }

    /// <summary>Matches compiled binding slots to active resource indices for diagnostics.</summary>
    private static Dictionary<string, int> Blocks(int program, ProgramInterface type, IEnumerable<KeyValuePair<string, int>> metadata)
    {
        var byBinding = new Dictionary<int, int>();
        GL.GetProgramInterface(program, type, ProgramInterfaceParameter.ActiveResources, out int count);
        for (int i = 0; i < count; i++)
        {
            int[] value = new int[1]; GL.GetProgramResource(program, type, i, 1, [ProgramProperty.BufferBinding], 1, out _, value);
            if (!byBinding.TryAdd(value[0], i)) throw new InvalidOperationException("Ambiguous SPIR-V block binding: " + value[0]);
        }
        return metadata.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => byBinding.GetValueOrDefault(g.First().Value, -1), StringComparer.Ordinal);
    }

    #endregion

    #region Lookup
    /// <summary>Returns active standalone locations from the compiled interface.</summary>
    public int GetUniformLocation(string name)
    {

        if (resources.Uniforms.TryGetValue(name, out int location)) return location;
        int bracket = name.IndexOf('[');
        if (bracket > 0 && name.EndsWith(']') && int.TryParse(name[(bracket + 1)..^1], out int index) &&
            resources.Uniforms.TryGetValue(name[..bracket], out location) && location >= 0 && index >= 0 &&
            index < resources.ArraySizes.GetValueOrDefault(location)) return location + index;
        return -1;
    }
    /// <summary>Returns a uniform block's numeric index, independent of optional shader names.</summary>
    public int GetUniformBlockIndex(string name) => resources.Blocks.GetValueOrDefault(name, -1);
    /// <summary>Returns a storage block's numeric index, independent of optional shader names.</summary>
    public int GetStorageBlockIndex(string name) => resources.Storage.GetValueOrDefault(name, -1);
    /// <summary>Resolves resource names from the generated contract for diagnostic cache enumeration.</summary>
    public void GetProgramResourceName(ProgramInterface kind, int index, int capacity, out int length, out string name)
    {
        var r = resources;
        if (kind == ProgramInterface.Uniform)
        {
            int[] location = new int[1]; GL.GetProgramResource(program, kind, index, 1, [ProgramProperty.Location], 1, out _, location);
            name = location[0] < 0 ? "" : r.Uniforms.FirstOrDefault(p => p.Value == location[0]).Key ?? "";
        }
        else name = (kind == ProgramInterface.UniformBlock ? r.Blocks : r.Storage).FirstOrDefault(p => p.Value == index).Key ?? "";
        length = name.Length;
    }
    /// <summary>Provides the inactive entries required by the engine's indexed uniform dictionary.</summary>
    public IReadOnlyDictionary<string, int> Uniforms => resources.Uniforms;
    #endregion
}


