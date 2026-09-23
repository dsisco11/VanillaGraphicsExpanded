using VanillaGraphicsExpanded.Rendering.Shaders;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Runs direct irradiance consumers against the same controlled cache and published voxel scene.</summary>
public abstract class DirectWorldProbeVisibilityTestBase : LumOnShaderFunctionalTestBase
{
    // The fixture owns production programs until disposal. Multi-frame scenarios
    // reuse the same compiled variant, matching runtime behavior instead of relinking each draw.
    private readonly Dictionary<string, GpuProgram> visibilityPrograms = new();
    /// <summary>Uses the shared mandatory GPU fixture.</summary>
    protected DirectWorldProbeVisibilityTestBase(HeadlessGLFixture fixture) : base(fixture) { }

    #region Direct Consumer Harness
    /// <summary>Renders debug modes, atlas gather (-1), or SH9 gather (-2), forcing invalid screen probes.</summary>
    private protected float[] RenderDirectVisibility(WorldProbeAtlasData atlas, ControlledTraceGpuScene? scene,
        Vector3 sampleCenter, Vector3 cacheOrigin, float spacing, int consumer,
        int size = 4, float span = 0.1f, int budget = 256, VectorInt3 worldOffset = default,
        Vector3 ring = default, bool suppress = false,
        Vector3[]? levelOrigins = null, Vector3[]? levelRings = null,
        Vector3d? playerOrigin = null, float cameraBob = 0,
        VanillaGraphicsExpanded.LumOn.Scene.Geometry.TraceGeometryGpuScene? shared = null,
        Vector3? receiverNormal = null)
    {
        bool debug = consumer >= 0;
        bool sh9 = consumer == -2;
        string shader = debug ? "lumon_debug_worldprobe" : sh9 ? "lumon_probe_sh9_gather" : "lumon_probe_atlas_gather";
        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"] = "1",
            ["VGE_LUMON_WORLDPROBE_ENABLED"] = "1",
            ["VGE_LUMON_WORLDPROBE_LEVELS"] = atlas.Levels.ToString(),
            ["VGE_LUMON_WORLDPROBE_RESOLUTION"] = atlas.Resolution.ToString(),
            ["VGE_LUMON_WORLDPROBE_BASE_SPACING"] = spacing.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["VGE_LUMON_WORLDPROBE_OCTAHEDRAL_SIZE"] = atlas.TileSize.ToString(),
            ["VGE_LUMON_WORLDPROBE_DIFFUSE_STRIDE"] = "2"
        };
        string key = shader + string.Join(";", defines.Select(pair => pair.Key + "=" + pair.Value));
        if (!visibilityPrograms.TryGetValue(key, out var program))
        {
            program = debug
                ? Programs.Create<LumOnDebugShaderProgram>(settings: defines, identity: LumOnDebugShaderProgram.WorldprobeContract.Identity)
                : sh9 ? Programs.Create<LumOnProbeSh9GatherShaderProgram>(settings: defines)
                : Programs.Create<LumOnScreenProbeAtlasGatherShaderProgram>(settings: defines);
            visibilityPrograms.Add(key, program);
        }
        using var use = program.UseScope();
        using var assets = new BinaryShaderApiFixture();
        int guideSize = debug ? size : size * 2;
        using var terrain = new EngineTerrainBuffers(guideSize,guideSize);
        using var terrainAttachments = new GBufferTextures(guideSize,guideSize);
        using var world = new VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu.LumOnWorldProbeClipmapGpuResources(atlas.Resolution,atlas.Levels,atlas.TileSize);
        var config = new VgeConfig(); config.LumOn.ProbeSpacingPx = Math.Max(1,guideSize/2);
        using var screen = new LumOnBufferManager(assets.Api,config);
        screen.EnsureBuffers(guideSize,guideSize);
        {
            // Match production sampler units, including SH9's sixteen-unit limit.
            // Gather uses integer full-resolution guide fetches; every addressed texel
            // must exist rather than relying on undefined out-of-range sampling.

            var depth = new float[guideSize * guideSize];
            Array.Fill(depth, 0.5f);
            var normals = new float[depth.Length * 4];
            for (int i = 0; i < normals.Length; i += 4)
            {
                var normal = receiverNormal ?? -Vector3.UnitZ;
                normals[i] = normal.X * .5f + .5f;
                normals[i + 1] = normal.Y * .5f + .5f;
                normals[i + 2] = normal.Z * .5f + .5f;
                normals[i + 3] = 1;
            }
            terrain.Depth.UploadDataImmediate(depth);
            terrainAttachments.Normal.UploadDataImmediate(normals);
            world.ProbeRadianceAtlas.UploadDataImmediate(atlas.Radiance);
            world.ProbeVis0.UploadDataImmediate(atlas.Visibility);
            world.ProbeMeta0.UploadDataImmediate(atlas.Metadata);
            screen.ProbeAnchorPositionTex!.UploadDataImmediate(new float[screen.ProbeAnchorPositionTex.Width * screen.ProbeAnchorPositionTex.Height * 4]);
            screen.ProbeAnchorNormalTex!.UploadDataImmediate(new float[screen.ProbeAnchorNormalTex.Width * screen.ProbeAnchorNormalTex.Height * 4]);
            var geometry = shared ?? scene?.Backend;
            switch (program)
            {
                case LumOnDebugShaderProgram viewProgram:
                    viewProgram.PrimaryDepth = terrain.Depth.TextureId;
                    viewProgram.GBufferNormal = terrainAttachments.Normal.TextureId;
                    viewProgram.WorldProbeRadianceAtlas = world.ProbeRadianceAtlas;
                    viewProgram.WorldProbeVis0 = world.ProbeVis0;
                    viewProgram.WorldProbeMeta0 = world.ProbeMeta0;
                    viewProgram.NearFieldVisibility.Bind(viewProgram, geometry);
                    viewProgram.DebugMode = consumer;
                    break;
                case LumOnProbeSh9GatherShaderProgram gather:
                    for (int i = 0; i < 7; i++) ((DynamicTexture2D)screen.ProbeSh9Fbo![i]).UploadDataImmediate(new float[screen.ProbeAnchorPositionTex.Width * screen.ProbeAnchorPositionTex.Height * 4]);
                    gather.ProbeSh0 = screen.ProbeSh9Fbo![0]; gather.ProbeSh1 = screen.ProbeSh9Fbo[1];
                    gather.ProbeSh2 = screen.ProbeSh9Fbo[2]; gather.ProbeSh3 = screen.ProbeSh9Fbo[3];
                    gather.ProbeSh4 = screen.ProbeSh9Fbo[4]; gather.ProbeSh5 = screen.ProbeSh9Fbo[5]; gather.ProbeSh6 = screen.ProbeSh9Fbo[6];
                    gather.PrimaryDepth = terrain.Depth.TextureId; gather.GBufferNormal = terrainAttachments.Normal.TextureId;
                    gather.ProbeAnchorPosition = screen.ProbeAnchorPositionTex; gather.ProbeAnchorNormal = screen.ProbeAnchorNormalTex;
                    gather.WorldProbeRadianceAtlas = world.ProbeRadianceAtlas; gather.WorldProbeVis0 = world.ProbeVis0; gather.WorldProbeMeta0 = world.ProbeMeta0;
                    gather.NearFieldVisibility.Bind(gather, geometry);
                    gather.Intensity = 1; gather.IndirectTint = [1,1,1]; gather.SuppressWorldProbeRadiance = suppress;
                    break;
                case LumOnScreenProbeAtlasGatherShaderProgram gather:
                    screen.ScreenProbeAtlasFilteredTex!.UploadDataImmediate(new float[screen.ScreenProbeAtlasFilteredTex.Width * screen.ScreenProbeAtlasFilteredTex.Height * 4]);
                    gather.ScreenProbeAtlas = screen.ScreenProbeAtlasFilteredTex;
                    gather.PrimaryDepth = terrain.Depth.TextureId; gather.GBufferNormal = terrainAttachments.Normal.TextureId;
                    gather.ProbeAnchorPosition = screen.ProbeAnchorPositionTex; gather.ProbeAnchorNormal = screen.ProbeAnchorNormalTex;
                    gather.WorldProbeRadianceAtlas = world.ProbeRadianceAtlas; gather.WorldProbeVis0 = world.ProbeVis0; gather.WorldProbeMeta0 = world.ProbeMeta0;
                    gather.NearFieldVisibility.Bind(gather, geometry);
                    gather.Intensity = 1; gather.IndirectTint = [1,1,1]; gather.SampleStride = 1; gather.SuppressWorldProbeRadiance = suppress;
                    break;
            }
            // Component scenarios override the traversal budget through the existing
            // parameter contract. Production scene binding still owns every sampler.
            var local = new LumOnNearFieldParamsUbo();
            if (shared != null) local.SetShared(shared, budget);
            else local.Set(scene?.Origin ?? default, scene?.Resolution ?? 0, budget, scene?.CellSize ?? 16);
            local.BindTo(program, LumOnNearFieldParamsUbo.BlockName, "Tests.DirectVisibility");
            // Move the camera while compensating the view-space receiver so the
            // reconstructed player-relative surface remains stationary.
            float cameraHeight = playerOrigin.HasValue ? 1.625f : 0;
            float viewY = cameraHeight + cameraBob;
            float[] inverseView = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,viewY,0,1];
            float[] view = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,-viewY,0,1];
            var origin = playerOrigin ?? new Vector3d(worldOffset.X, worldOffset.Y, worldOffset.Z);
            var bridge = LumOnFrameWorldSpaceBridge.Compute(origin.X, origin.Y, origin.Z);
            float[] inverse = [span,0,0,0, 0,span,0,0, 0,0,1,0, sampleCenter.X,sampleCenter.Y-viewY,sampleCenter.Z,1];
            UpdateAndBindLumOnFrameUbo(program, invProjectionMatrix: inverse,
                invViewMatrix: inverseView, viewMatrix: view,
                screenWidth: debug ? size : size * 2, screenHeight: debug ? size : size * 2,
                matrixSpaceWorldChunkCoordOffset: bridge.ChunkOffset,
                matrixSpaceWorldBlockOffsetRem: bridge.BlockOffsetRemainder);
            UpdateAndBindLumOnWorldProbeUbo(program, new Vec3f(), Vector3.Zero, levelOrigins ?? [cacheOrigin], levelRings ?? [ring]);
            var output = debug ? terrain.Output : screen.IndirectHalfFbo!;
            TestFramework.RenderQuadTo(program, output);
            return output[0].ReadPixels();
        }

    }
    #endregion
}
