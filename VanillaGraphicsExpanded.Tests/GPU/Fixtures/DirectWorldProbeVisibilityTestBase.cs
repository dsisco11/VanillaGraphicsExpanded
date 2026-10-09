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
    private readonly Dictionary<string, LumOnShaderProgram> visibilityPrograms = new();
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
        Vector3? receiverNormal = null, ShaderLightingResources? resources = null)
    {
        bool debug = consumer >= 0;
        bool sh9 = consumer == -2;
        string shader = debug ? LumOnDebugShaderProgramFamily.GetProgramName((LumOnDebugMode)consumer) : sh9 ? "lumon_probe_sh9_gather" : "lumon_probe_atlas_gather";
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
                ? Programs.Create<LumOnDebugShaderProgram>(settings: defines, identity: shader)
                : sh9 ? Programs.Create<LumOnProbeSh9GatherShaderProgram>(settings: defines)
                : Programs.Create<LumOnScreenProbeAtlasGatherShaderProgram>(settings: defines);
            visibilityPrograms.Add(key, program);
        }
        resources ??= LightingResources;
        int guideSize = debug ? size : size * 2;
        resources.Scene.EnsureSize(guideSize, guideSize);
        var terrain = resources.Scene.Engine;
        var terrainAttachments = resources.Scene.Terrain;
        var world = resources.EnsureWorldProbes(atlas.Resolution, atlas.Levels, atlas.TileSize);
        // The debug consumer does not read screen-probe resources, so it never allocates that family.
        var screen = debug ? null : resources.EnsureScreen(guideSize, guideSize, Math.Max(1, guideSize / 2));
        {
            // Use the production grouped sampler contract.
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
            terrainAttachments.Surface.UploadDataImmediate(normals, 0, 0, 0, guideSize, guideSize, 1);
            world.ProbeRadianceAtlas.UploadDataImmediate(atlas.Radiance);
            world.ProbeVis0.UploadDataImmediate(atlas.Visibility);
            world.ProbeMeta0.UploadDataImmediate(atlas.Metadata);
            if (screen != null)
            {
                screen.ProbeAnchors!.UploadDataImmediate(new float[screen.ProbeAnchors.Width * screen.ProbeAnchors.Height * 8], 0, 0, 0, screen.ProbeAnchors.Width, screen.ProbeAnchors.Height, 2);
            }
            var geometry = shared ?? scene?.Backend;
            var traceSettings = shared is null
                ? new LumOnNearFieldTraceSettings(budget)
                : new LumOnNearFieldTraceSettings(budget, shared.Coverage?.NearField, float.MaxValue);
            switch (program)
            {
                case LumOnDebugShaderProgram viewProgram:
                    viewProgram.PrimaryDepth = terrain.Depth.TextureId;
                    viewProgram.GBufferSurface = terrainAttachments.Surface;
                    viewProgram.WorldProbeRadianceAtlas = world.ProbeRadianceAtlas;
                    viewProgram.WorldProbeVis0 = world.ProbeVis0;
                    viewProgram.WorldProbeMeta0 = world.ProbeMeta0;
                    viewProgram.NearFieldVisibility.Stage(viewProgram, geometry, traceSettings);
                    viewProgram.DebugMode = consumer;
                    break;
                case LumOnProbeSh9GatherShaderProgram gather:
                    screen!.ProbeSh9!.UploadDataImmediate(new float[screen.ProbeSh9.Width * screen.ProbeSh9.Height * 28], 0, 0, 0, screen.ProbeSh9.Width, screen.ProbeSh9.Height, 7);
                    gather.ProbeSh9 = screen.ProbeSh9;
                    gather.PrimaryDepth = terrain.Depth.TextureId; gather.GBufferSurface = terrainAttachments.Surface;
                    gather.ProbeAnchors = screen.ProbeAnchors;
                    gather.WorldProbeRadianceAtlas = world.ProbeRadianceAtlas; gather.WorldProbeVis0 = world.ProbeVis0; gather.WorldProbeMeta0 = world.ProbeMeta0;
                    gather.NearFieldVisibility.Stage(gather, geometry, traceSettings);
                    gather.Intensity = 1; gather.IndirectTint = [1,1,1]; gather.SuppressWorldProbeRadiance = suppress;
                    break;
                case LumOnScreenProbeAtlasGatherShaderProgram gather:
                    screen!.ScreenProbeAtlasFilteredTex!.UploadDataImmediate(new float[screen!.ScreenProbeAtlasFilteredTex.Width * screen!.ScreenProbeAtlasFilteredTex.Height * 4]);
                    gather.ScreenProbeAtlas = screen!.ScreenProbeAtlasFilteredTex;
                    gather.PrimaryDepth = terrain.Depth.TextureId; gather.GBufferSurface = terrainAttachments.Surface;
                    gather.ProbeAnchors = screen.ProbeAnchors;
                    gather.WorldProbeRadianceAtlas = world.ProbeRadianceAtlas; gather.WorldProbeVis0 = world.ProbeVis0; gather.WorldProbeMeta0 = world.ProbeMeta0;
                    gather.NearFieldVisibility.Stage(gather, geometry, traceSettings);
                    gather.Intensity = 1; gather.IndirectTint = [1,1,1]; gather.SampleStride = 1; gather.SuppressWorldProbeRadiance = suppress;
                    break;
            }
            // Move the camera while compensating the view-space receiver so the
            // reconstructed player-relative surface remains stationary.
            float cameraHeight = playerOrigin.HasValue ? 1.625f : 0;
            float viewY = cameraHeight + cameraBob;
            float[] inverseView = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,viewY,0,1];
            float[] view = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,-viewY,0,1];
            var origin = playerOrigin ?? new Vector3d(worldOffset.X, worldOffset.Y, worldOffset.Z);
            var bridge = FrameWorldSpaceBridge.Compute(origin.X, origin.Y, origin.Z);
            float[] inverse = [span,0,0,0, 0,span,0,0, 0,0,1,0, sampleCenter.X,sampleCenter.Y-viewY,sampleCenter.Z,1];
            UpdateAndBindLumOnFrameUbo(program, invProjectionMatrix: inverse,
                invViewMatrix: inverseView, viewMatrix: view,
                screenWidth: debug ? size : size * 2, screenHeight: debug ? size : size * 2,
                matrixSpaceWorldChunkCoordOffset: bridge.ChunkOffset,
                matrixSpaceWorldBlockOffsetRem: bridge.BlockOffsetRemainder);
            UpdateAndBindLumOnWorldProbeUbo(program, new Vec3f(), Vector3.Zero, levelOrigins ?? [cacheOrigin], levelRings ?? [ring]);
            var output = debug ? terrain.Output : screen!.IndirectHalfFbo!;
            TestFramework.RenderQuadTo(program, output);
            return output[0].ReadPixels();
        }

    }
    #endregion
}
