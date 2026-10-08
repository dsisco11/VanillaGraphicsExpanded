using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.WorldProbes;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.Client.NoObf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Captures oriented water boundaries before the existing scene-linear opaque composite.</summary>
internal sealed class WaterVolumeRenderer : IRenderer
{
    private readonly ICoreClientAPI api;
    private readonly LiquidGraphicsSubmission submission;
    private readonly Action unregisterResize;
    private static WaterVolumeRenderer? active;
    private GpuFramebuffer? target;
    private Texture3D? transport;
    private GpuResourceCollection? targetResources;
    private WaterVolumeFrame? completed;
    private bool composed;
    private bool failed;
    public double RenderOrder => 10.5;
    public int RenderRange => int.MaxValue;

    #region Public API
    /// <summary>Registers before opaque composition without taking ownership of engine OIT or liquid depth.</summary>
    internal WaterVolumeRenderer(ICoreClientAPI api)
    {
        this.api = api;
        submission = new(api);
        active = this;
        unregisterResize = ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder, Retire);
        api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "vge_water_volume");
        api.Event.LeaveWorld += LeaveWorld;
    }

    /// <summary>Returns only a completed capture belonging to the current API/world invocation.</summary>
    internal static bool TryGetFrame(ICoreClientAPI api, out WaterVolumeFrame frame)
    {
        frame = default;
        if (active is null || !ReferenceEquals(active.api, api) || active.completed is not { } captured) return false;
        frame = captured;
        return true;
    }
    /// <summary>Signals that the opaque renderer published transport through its single display boundary.</summary>
    internal static void MarkComposed(ICoreClientAPI api)
    {
        if (active is not null && ReferenceEquals(active.api, api) && active.completed.HasValue) active.composed = true;
    }
    /// <summary>Allows interface-only OIT only after the matching opaque frame actually consumed the capture.</summary>
    internal static bool WasComposed(ICoreClientAPI api)
        => active is not null && ReferenceEquals(active.api, api) && active.composed && active.completed.HasValue;

    /// <summary>Rasterizes all oriented transmitting boundaries with additive blending and opaque-depth clipping.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        completed = null;
        composed = false;
        if (stage != EnumRenderStage.Opaque || failed || !LiquidDepthRenderer.TryGetCompletedWaveFrame(out var waves)
            || !LiquidMeshSource.TryGet(api, out var source) || !source.TryGetAtlasPools(out var atlases, out var pools)
            || !LiquidRenderer.CanTakeOwnership(api, atlases) || atlases.Length == 0) return;
        var primary = api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primary is null || primary.Width <= 0 || primary.Height <= 0 || primary.DepthTextureId == 0) return;
        var program = GpuShaderPrograms.Get<LiquidShaderProgram>(api, LiquidShaderProgram.VolumePassName);
        if (program is null) return;
        try
        {
            if (target is not null && (target.Width != primary.Width || target.Height != primary.Height)) Retire();
            if (target is null)
            {
                var resources = new GpuResourceCollection();
                try
                {
                    transport = resources.Own(Texture3D.Create(primary.Width, primary.Height, 2, PixelInternalFormat.Rgba32f,
                        TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Water.Transport"));
                    var optical = resources.Own(GpuFramebufferAttachment.FromTexture(transport, layer: WaterVolumeFrame.OpticalLayer));
                    var illumination = resources.Own(GpuFramebufferAttachment.FromTexture(transport, layer: WaterVolumeFrame.SourceLayer));
                    target = resources.Own(GpuFramebuffer.Create([optical, illumination]));
                    targetResources = resources;
                }
                catch { resources.Dispose(); throw; }
            }
            if (!program.EnsureReady()) return;
            program.CaptureFrameInputs(api, source.TileSize);
            program.VolumeTransportEnabled = false;
            program.RefractionEnabled = false;
            program.WaveFrame = waves;
            program.ModelViewMatrix = api.Render.CameraMatrixOriginf;
            program.ForcedTransparency = 0;
            program.DepthTexture = SceneColor.SceneColorParticleCapture.ReceiverDepth(api, primary.DepthTextureId);
            program.ShadowMapNear = api.Render.FrameBuffers[(int)EnumFrameBuffer.ShadowmapNear]?.DepthTextureId ?? 0;
            program.ShadowMapFar = api.Render.FrameBuffers[(int)EnumFrameBuffer.ShadowmapFar]?.DepthTextureId ?? 0;
            MaterialAtlasSystem.Instance.TextureStore.TryGetPageTextures(atlases[0], out var initialMaterial);
            program.TerrainTexture = atlases[0];
            program.MaterialParamsTexture = initialMaterial.MaterialParamsTexture;
            LiquidRenderer.BindWaterMedium(program, MaterialAtlasSystem.Instance.TextureStore, atlases[0]);
            program.LightShaftOcclusion = Postprocessing.LightShaftOcclusionRenderer.Texture;
            program.AerialRadianceTexture = ModSystems.AtmosphereModSystem.AerialRadianceTexture;
            program.AerialAttenuationTexture = ModSystems.AtmosphereModSystem.AerialAttenuationTexture;
            if (!submission.Run(program, pools[..atlases.Length], new(target, LiquidPipelineStates.VolumeOutputs),
                new(), LiquidPipelineStates.VolumeBlending, () =>
            {
                for (int i = 0; i < atlases.Length; i++)
                {
                    MaterialAtlasSystem.Instance.TextureStore.TryGetPageTextures(atlases[i], out var material);
                    program.TerrainTexture = atlases[i];
                    program.MaterialParamsTexture = material.MaterialParamsTexture;
                    LiquidRenderer.BindWaterMedium(program, MaterialAtlasSystem.Instance.TextureStore, atlases[i]);
                    pools[i].Render(api.World.Player.Entity.CameraPos, "origin", EnumFrustumCullMode.CullNormal);
                }
            })) return;
            if (!TryGetCameraMedium(out var medium, out var cameraSource)) return;
            completed = new(transport!, medium, cameraSource);
        }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            failed = true;
            api.Logger.Error("[VGE] Water volume disabled for this world: {0}", error.ToString());
        }
    }

    /// <summary>Unregisters and retires owned screen resources.</summary>
    public void Dispose()
    {
        api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        api.Event.LeaveWorld -= LeaveWorld;
        unregisterResize();
        submission.Dispose();
        Retire();
        if (ReferenceEquals(active, this)) active = null;
    }
    #endregion

    #region Private
    /// <summary>Uses the actual fluid layer and mapped texture rather than applying water optics to every submerged liquid.</summary>
    private bool TryGetCameraMedium(out WaterMedium? medium, out Vector3 source)
    {
        medium = null;
        source = Vector3.Zero;
        if (api.Render.ShaderUniforms.CameraUnderwater <= .7f) return true;
        var position = api.World.Player.Entity.CameraPos;
        var blockPosition = new BlockPos((int)Math.Floor(position.X), (int)Math.Floor(position.Y), (int)Math.Floor(position.Z),
            api.World.Player.Entity.Pos.Dimension);
        if (api.World.BlockAccessor.GetChunkAtBlockPos(blockPosition) is null)
            return false;
        var block = api.World.BlockAccessor.GetBlock(blockPosition, BlockLayersAccess.Fluid);
        if (!BlockFaceTextureKeyResolver.TryResolveBaseTextureLocation(block, 4, out var texture, out _)
            || !PbrMaterialRegistry.Instance.TryGetMaterial(texture, out var material) || material.Transmission <= 0) return true;
        medium = material.WaterMedium ?? WaterMedium.Clear;
        var light = api.World.BlockAccessor.GetLightRGBs(blockPosition);
        // Block light is local; sunlight brightness gates only the bounded diffuse environment.
        // No direct solar source is assumed without a camera shadow sample.
        var environment = ModSystems.AtmosphereModSystem.Lighting?.Environment ?? Vector3.Zero;
        source = medium.Value.EffectiveScatteringPerMetre * Vector3.Max(new Vector3(light.X, light.Y, light.Z)
            + environment * Math.Clamp(light.W, 0, 1), Vector3.Zero) / (4 * MathF.PI);
        return true;
    }

    /// <summary>Drops prior-world resources and permits a fresh preparation attempt.</summary>
    private void LeaveWorld() { Retire(); failed = false; }

    /// <summary>Invalidates publication before freeing or replacing its textures.</summary>
    private void Retire()
    {
        completed = null;
        composed = false;
        targetResources?.Dispose();
        targetResources = null;
        target = null;
        transport = null;
    }
    #endregion
}
