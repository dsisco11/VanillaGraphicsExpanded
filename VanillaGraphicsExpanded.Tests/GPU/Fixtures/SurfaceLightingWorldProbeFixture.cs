using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns the production world-probe atlas and its actual SPIR-V upload programs.</summary>
internal sealed class SurfaceLightingWorldProbeFixture : IDisposable
{
    private readonly BinaryShaderApiFixture assets=new();
    private readonly LumOnWorldProbeClipmapResolveShaderProgram metadata=new();
    private readonly LumOnWorldProbeRadianceTileResolveShaderProgram radiance=new();
    private readonly LumOnWorldProbeClipmapGpuUploader uploader;
    public LumOnWorldProbeClipmapGpuResources Resources { get; }

    #region Resource ownership
    /// <summary>Provides shader lookup at the engine boundary while retaining the production upload implementation.</summary>
    public SurfaceLightingWorldProbeFixture()
    {
        Resources=new(assets.Api,1,1,8);
        var shaders=RuntimeRenderEvents.Adapt<IShaderAPI>((method,args)=>method.Name=="GetProgramByName"
            ? (string)args![0]! == "lumon_worldprobe_clipmap_resolve" ? metadata : radiance
            : throw new NotSupportedException(method.Name));
        var api=RuntimeRenderEvents.Adapt<ICoreClientAPI>((method,args)=>method.Name=="get_Shader"?shaders:method.Invoke(assets.Api,args));
        metadata.PassName="lumon_worldprobe_clipmap_resolve";metadata.VertexShader=new Vintagestory.Client.NoObf.Shader();metadata.FragmentShader=new Vintagestory.Client.NoObf.Shader();
        radiance.PassName="lumon_worldprobe_radiance_tile_resolve";radiance.VertexShader=new Vintagestory.Client.NoObf.Shader();radiance.FragmentShader=new Vintagestory.Client.NoObf.Shader();
        metadata.Initialize(api);radiance.Initialize(api);
        Assert.True(metadata.CompileAndLink(),string.Join('\n',assets.Logs));Assert.True(radiance.CompileAndLink(),string.Join('\n',assets.Logs));
        uploader=new(api);
    }

    /// <summary>Publishes the actual resolved result; unresolved batches must be rejected by the caller.</summary>
    public float[] Upload(in LumOnWorldProbeTraceResult result)
    {
        Assert.True(result.Success);
        Assert.True(uploader.Upload(Resources,new[]{result},65536)>0);
        return Resources.ProbeRadianceAtlas.ReadPixels();
    }

    /// <summary>Attempts an atomic publication with an explicit budget and exposes its admission count.</summary>
    public int TryUpload(in LumOnWorldProbeTraceResult result, int budget)
        => uploader.Upload(Resources,new[]{result},budget);

    /// <summary>Retires uploads before deleting programs and their atlas resources.</summary>
    public void Dispose() { uploader.Dispose();Resources.Dispose();metadata.Dispose();radiance.Dispose();assets.Dispose(); }
    #endregion
}
