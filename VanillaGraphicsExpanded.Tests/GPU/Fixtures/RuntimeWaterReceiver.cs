using HarmonyLib;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Publishes an opaque HDR receiver through the actual direct and composite render owners.</summary>
internal sealed class RuntimeWaterReceiver : IDisposable
{
    private readonly RuntimeLightingPrograms programs = new();
    private readonly ShaderTestFramework drawing = new();
    private readonly EngineTerrainBuffers terrain;
    private readonly GBufferManager gbuffer;
    private readonly DirectLightingBufferManager directBuffers;
    private readonly DirectLightingRenderer direct;
    private readonly PBRCompositeRenderer composite;
    private readonly WaterRefractionCapture capture;
    public WaterRefractionScene Scene => composite.RefractionScene;

    #region Public API
    /// <summary>Captures emissive opaque geometry and publishes its full or reduced receiver pair.</summary>
    public RuntimeWaterReceiver(BinaryShaderApiFixture assets, int size, float[] projection, float depth, int backgroundScale)
    {
        terrain = new EngineTerrainBuffers(size, size);
        var events = new RuntimeRenderEvents();
        var framebuffers = Enumerable.Repeat<FrameBufferRef>(null!, Enum.GetValues<EnumFrameBuffer>().Max(value => (int)value) + 1).ToList();
        framebuffers[(int)EnumFrameBuffer.Primary] = terrain.Primary;
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        var render = RuntimeEngineServices.Render(size, framebuffers, () => identity, () => projection, drawing.RenderGeometry);
        var api = RuntimeRenderEvents.Adapt<ICoreClientAPI>((method, args) => method.Name switch
        {
            "get_Render" => render,
            "get_Event" => events.Api,
            "get_Shader" => programs.Api,
            _ => method.Invoke(assets.Api, args)
        });
        programs.Initialize(api);
        var config = new VanillaGraphicsExpanded.LumOn.VgeConfig();
        config.LumOn.Enabled = false;
        gbuffer = new GBufferManager(api);
        Assert.True(gbuffer.EnsureBuffers(size, size));
        directBuffers = new DirectLightingBufferManager(api);
        direct = new DirectLightingRenderer(api, gbuffer, directBuffers);
        composite = new PBRCompositeRenderer(api, gbuffer, directBuffers, config, () => null);
        capture = new WaterRefractionCapture(api, direct, composite);
        var settings = VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config;
        bool previousEnabled = settings.WaterRefractionEnabled;
        int previousScale = settings.WaterRefractionBackgroundScale;
        try
        {
            settings.WaterRefractionEnabled = true;
            settings.WaterRefractionBackgroundScale = 3 - backgroundScale;
            // Emission gives this receiver a known nonzero HDR source without a world lighting service.
            terrain.UploadTerrain(gbuffer, Enumerable.Repeat(depth, size * size).ToArray(),
                Enumerable.Range(0, size * size).SelectMany(_ => new float[] { .5f, .5f, 1, 1 }).ToArray(),
                Enumerable.Range(0, size * size).SelectMany(_ => new float[] { .5f, 0, 4, 0 }).ToArray(),
                Enumerable.Range(0, size * size).SelectMany(_ => new float[] { 1, .5f, .25f, 1 }).ToArray());
            AccessTools.Method(typeof(WaterRefractionCapture), "Capture").Invoke(capture, null);
            Assert.True(composite.PreOverlayScene!.Published);
            direct.OnRenderFrame(.016f, EnumRenderStage.Opaque);
            composite.OnRenderFrame(.016f, EnumRenderStage.Opaque);
            Assert.True(Scene.Published);
            Assert.Equal(backgroundScale, Scene.BackgroundScale);
        }
        finally
        {
            settings.WaterRefractionEnabled = previousEnabled;
            settings.WaterRefractionBackgroundScale = previousScale;
        }
    }

    /// <summary>Retires consumers before their framebuffer and executable owners.</summary>
    public void Dispose()
    {
        capture.Dispose(); composite.Dispose(); direct.Dispose(); directBuffers.Dispose();
        gbuffer.Dispose(); terrain.Dispose(); drawing.Dispose(); programs.Dispose();
    }
    #endregion
}
