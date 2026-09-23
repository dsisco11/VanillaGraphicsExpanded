using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Supplies the engine shader registry with actual production program instances and built binaries.</summary>
internal sealed class RuntimeLightingPrograms : IDisposable
{
    private readonly Dictionary<string,GpuProgram> programs = new();
    public ICoreClientAPI Api { private get; set; } = null!;
    public IReadOnlyCollection<string> Loaded => programs.Keys;

    #region Program ownership
    /// <summary>Creates each production program lazily at the same lookup boundary used by the renderer.</summary>
    public GpuProgram Get(string name)
    {
        if(programs.TryGetValue(name,out var existing)) return existing;
        GpuProgram program = name switch
        {
            "lumon_velocity" => new LumOnVelocityShaderProgram(),
            "lumon_probe_anchor" => new LumOnProbeAnchorShaderProgram(),
            "lumon_probe_atlas_pis_mask" => new LumOnProbeAtlasPisMaskShaderProgram(),
            "lumon_probe_atlas_trace" => new LumOnScreenProbeAtlasTraceShaderProgram(),
            "lumon_probe_atlas_temporal" => new LumOnScreenProbeAtlasTemporalShaderProgram(),
            "lumon_probe_atlas_filter" => new LumOnScreenProbeAtlasFilterShaderProgram(),
            "lumon_probe_atlas_project_sh9" => new LumOnScreenProbeAtlasProjectSh9ShaderProgram(),
            "lumon_probe_atlas_gather" => new LumOnScreenProbeAtlasGatherShaderProgram(),
            "lumon_probe_sh9_gather" => new LumOnProbeSh9GatherShaderProgram(),
            "lumon_upsample" => new LumOnUpsampleShaderProgram(),
            "lumon_hzb_copy" => new LumOnHzbCopyShaderProgram(),
            "lumon_hzb_downsample" => new LumOnHzbDownsampleShaderProgram(),
            "lumon_worldprobe_clipmap_resolve" => new LumOnWorldProbeClipmapResolveShaderProgram(),
            "lumon_worldprobe_radiance_tile_resolve" => new LumOnWorldProbeRadianceTileResolveShaderProgram(),
            _ => throw new NotSupportedException(name)
        };
        program.PassName=name;
        program.VertexShader=new Vintagestory.Client.NoObf.Shader();
        program.FragmentShader=new Vintagestory.Client.NoObf.Shader();
        program.Initialize(Api);
        Assert.True(program.CompileAndLink(),$"Cannot load production shader {name}");
        programs.Add(name,program);
        return program;
    }

    /// <summary>Retires all programs after the renderers stop submitting work.</summary>
    public void Dispose() { foreach(var program in programs.Values) program.Dispose(); programs.Clear(); }
    #endregion
}
