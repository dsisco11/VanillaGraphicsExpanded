using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Composes registered cache, screen-probe and worker-driven world-probe consumers against controlled engine inputs.</summary>
internal sealed class SurfaceLightingConsumerRuntimeFixture : IDisposable
{
    private readonly EngineShaderPlatformScope platform = new();
    private readonly ShaderTestFramework drawing = new();
    private readonly RuntimeLightingPrograms programs = new();
    private readonly VanillaGraphicsExpanded.ModSystems.WorldProbeModSystem worldSystem = new();
    private readonly LumOnRenderer renderer;
    private readonly float[] projection = LumOnTestInputFactory.CreateRealisticProjection();
    public SurfaceCacheRuntimeFixture Cache { get; }
    public RuntimeProbeWorld World { get; }
    public LumOnBufferManager Screen { get; }
    public LumOnWorldProbeClipmapBufferManager WorldBuffers { get; }
    public LumOnWorldProbeUpdateRenderer WorldRenderer { get; }
    public List<int> DrawnPrograms { get; } = [];
    public IReadOnlyCollection<string> LoadedPrograms => programs.Loaded;
    public const int FrameBudget = 160;

    #region Composition
    /// <summary>Registers real consumers with the producer's engine events and injects real publication providers.</summary>
    public SurfaceLightingConsumerRuntimeFixture(bool sh9)
    {
        Cache = new(requestedPages:24,enclosure:true);
        World = new(Cache.SourceBlock);
        var cfg=Cache.Config.LumOn;
        cfg.ProbeSpacingPx=1; cfg.ProbeAtlasTexelsPerFrame=8; cfg.RayMaxDistance=16;
        cfg.AnchorJitterEnabled=false; cfg.EnableProbePIS=false; cfg.EnableReprojectionVelocity=false;
        cfg.Intensity=1; cfg.IndirectTint=[1,1,1]; cfg.LumonScene.RelightMaxPagesPerFrame=4;
        cfg.ProbeAtlasGather=sh9?VgeConfig.ProbeAtlasGatherMode.EvaluateProjectedSH:VgeConfig.ProbeAtlasGatherMode.IntegrateAtlas;
        var wp=Cache.Config.WorldProbeClipmap;
        wp.ClipmapResolution=1; wp.ClipmapLevels=1; wp.ClipmapBaseSpacing=4;
        wp.OctahedralTileSize=8; wp.AtlasTexelsPerUpdate=64; wp.TraceMaxProbesPerFrame=1;
        wp.PerLevelProbeUpdateBudget=[1]; wp.UploadBudgetBytesPerFrame=1576; wp.EnableDirectionPIS=false;
        /// <summary>Supplies an engine camera positioned inside the controlled enclosure.</summary>
        LumOnCameraState? Camera() => new(4,36,6,4,36,6,0);
        var world=RuntimeRenderEvents.Adapt<IClientWorldAccessor>((method,_)=>method.Name switch
        {
            "get_Player"=>null,"get_BlockAccessor"=>World.Accessor,"get_Calendar"=>null,"get_MapSizeY"=>256,
            _=>throw new NotSupportedException(method.Name)
        });
        var render=RuntimeRenderEvents.Adapt<IRenderAPI>((method,args)=>method.Name switch
        {
            "get_CurrentProjectionMatrix"=>projection,
            "UploadMesh"=>new RuntimeMesh(),
            "DeleteMesh"=>DeleteMesh((MeshRef)args![0]!),
            "RenderMesh"=>Draw(),
            "GlToggleBlend"=>Blend((bool)args![0]!),
            _=>method.Invoke(Cache.Api.Render,args)
        });
        var shader=RuntimeRenderEvents.Adapt<IShaderAPI>((method,args)=>method.Name=="GetProgramByName"?programs.Get((string)args![0]!):throw new NotSupportedException(method.Name));
        var input=RuntimeRenderEvents.Adapt<IInputAPI>((method,_)=>method.Name is "RegisterHotKey" or "SetHotKeyHandler"?null:throw new NotSupportedException(method.Name));
        var mods=RuntimeRenderEvents.Adapt<IModLoader>((method,_)=>method.Name=="GetModSystem" && method.GetGenericArguments().Single()==typeof(VanillaGraphicsExpanded.ModSystems.WorldProbeModSystem)?worldSystem:throw new NotSupportedException(method.Name));
        var api=RuntimeRenderEvents.Adapt<ICoreClientAPI>((method,args)=>method.Name switch
        {
            "get_ModLoader"=>mods,"get_World"=>world,"get_Render"=>render,"get_Shader"=>shader,"get_Input"=>input,
            _=>method.Invoke(Cache.Api,args)
        });
        programs.Api=api;
        Screen=new(api,Cache.Config); WorldBuffers=new(api,Cache.Config);
        WorldRenderer=new(api,Cache.Config,WorldBuffers,Camera);
        WorldRenderer.SetSurfaceLightingProvider(Cache.LightingProvider,Cache.Geometry);
        renderer=new(api,Cache.Config,Screen,Cache.Buffers,WorldBuffers,Camera);
        renderer.SetNearFieldSceneProvider(Cache.Geometry); renderer.SetSurfaceLightingProvider(Cache.LightingProvider);
    }
    #endregion

    #region Frames and observations
    /// <summary>Supplies terrain raster data, then invokes the registered engine stages without calling individual lighting passes.</summary>
    public void Frame()
    {
        var primary=Cache.Api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        float z=-5,depth=(projection[10]*z+projection[14])/(projection[11]*z+projection[15])*.5f+.5f;
        Upload(primary.DepthTextureId,PixelFormat.Red,Enumerable.Repeat(depth,4).ToArray());
        Upload(Cache.Buffers.NormalTextureId,PixelFormat.Rgba,Enumerable.Range(0,4).SelectMany(_=>new[]{.5f,.5f,1f,0f}).ToArray());
        Upload(Cache.Buffers.MaterialTextureId,PixelFormat.Rgba,Enumerable.Range(0,4).SelectMany(_=>new[]{1f,0f,0f,0f}).ToArray());
        Upload(primary.ColorTextureIds[0],PixelFormat.Rgba,Enumerable.Repeat(.5f,16).ToArray());
        Cache.Frame();
        if(Screen.IndirectFullTex!=null) Energy(FinalPixels());
        if(WorldBuffers.Resources!=null) Energy(WorldPixels());
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Advances a bounded number of real frames, allowing asynchronous workers CPU time between callbacks.</summary>
    public void RunUntil(Func<bool> condition,int maximumFrames=FrameBudget)
    {
        for(int i=0;i<maximumFrames&&!condition();i++) { Frame(); Thread.Sleep(1); }
        Assert.True(condition(),$"Runtime failed to settle in {maximumFrames} frames; frames={Cache.Frames}, workerReads={World.WorkerReads}, final={Energy(FinalPixels())}, world={Energy(WorldPixels())}, worldConfidence={WorldBuffers.Resources!.ProbeMeta0.ReadPixels()[0]}, trace={Energy(Screen.ScreenProbeAtlasHistoryTex!.ReadPixels())}, filter={Energy(Screen.ScreenProbeAtlasFilteredTex!.ReadPixels())}, gather={Energy(Screen.IndirectHalfTex!.ReadPixels())}, anchors={string.Join(",",Screen.ProbeAnchorPositionTex!.ReadPixels())}, pending={WorldRenderer.HasPendingSurfaceLightingQueries}, programs={string.Join(',',LoadedPrograms)}, logs={string.Join('|',Cache.Logs.TakeLast(8))}");
    }

    /// <summary>Reads the final full-resolution indirect output that the renderer publishes to composition.</summary>
    public float[] FinalPixels() => Screen.IndirectFullTex?.ReadPixels() ?? [];

    /// <summary>Reads the actual worker/query/upload-owned world radiance atlas.</summary>
    public float[] WorldPixels() => WorldBuffers.Resources?.ProbeRadianceAtlas.ReadPixels() ?? [];

    /// <summary>Checks every RGB channel and returns the peak finite energy at an observable boundary.</summary>
    public static float Energy(float[] pixels)
    {
        Assert.NotEmpty(pixels); float peak=0;
        for(int i=0;i<pixels.Length;i++)
        {
            Assert.True(float.IsFinite(pixels[i]));
            if(i%4!=3) peak=Math.Max(peak,Math.Abs(pixels[i]));
        }
        return peak;
    }

    /// <summary>Uploads only engine raster inputs; no probe or cache radiance is synthesized.</summary>
    private static void Upload(int texture,PixelFormat format,float[] values)
    {
        using var binding=GlStateCache.Current.BindTextureScope(TextureTarget.Texture2D,0,texture);
        GL.TexSubImage2D(TextureTarget.Texture2D,0,0,0,2,2,format,PixelType.Float,values);
    }

    /// <summary>Submits the currently bound production shader through a real GPU fullscreen primitive.</summary>
    private object? Draw()
    {
        int program=GL.GetInteger(GetPName.CurrentProgram); Assert.NotEqual(0,program);
        DrawnPrograms.Add(program); drawing.RenderQuad(program); return null;
    }
    /// <summary>Implements the engine blend-state boundary.</summary>
    private static object? Blend(bool enabled) { if(enabled) GL.Enable(EnableCap.Blend); else GL.Disable(EnableCap.Blend); return null; }
    /// <summary>Releases the engine mesh token.</summary>
    private static object? DeleteMesh(MeshRef mesh) { mesh.Dispose(); return null; }
    #endregion

    #region Disposal
    /// <summary>Unblocks workers before retiring consumers, then their producer and engine dependencies.</summary>
    public void Dispose()
    {
        World.ReleaseWorker(); WorldRenderer.Dispose(); renderer.Dispose();
        WorldBuffers.Dispose(); Screen.Dispose(); programs.Dispose(); drawing.Dispose(); World.Dispose(); worldSystem.Dispose(); Cache.Dispose(); platform.Dispose();
    }
    /// <summary>Represents the engine mesh while draws use the controlled GPU triangle.</summary>
    private sealed class RuntimeMesh : MeshRef { public override bool Initialized=>!Disposed; }
    #endregion
}
