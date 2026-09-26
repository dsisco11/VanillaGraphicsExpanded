using System.Numerics;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Production shader bindings shared by the near-field scenarios.</summary>
public abstract class NearFieldShaderTestBase : LumOnShaderFunctionalTestBase
{
    #region Scenario programs
    /// <summary>Identifies every variable shader selection authored by this harness; per-draw inputs remain uncached.</summary>
    private readonly record struct TraceProgramKey(bool NearField, float EmissiveBoost, int TexelsPerFrame,
        bool WorldProbes, int WorldResolution, float WorldSpacing, int WorldTileSize, float RayMaxDistance);

    // Match the direct-visibility harness: reuse only within this test, with disposal owned by Programs.
    private readonly Dictionary<TraceProgramKey, LumOnScreenProbeAtlasTraceShaderProgram> tracePrograms = new();
    #endregion

    /// <summary>Uses the shared headless GPU context.</summary>
    protected NearFieldShaderTestBase(HeadlessGLFixture fixture) : base(fixture) { }

    #region Shader Harness
    /// <summary>Runs the production shader with the production sixteen-unit texture layout.</summary>
    private protected (float[] Radiance, float[] Meta) Trace(NearFieldVoxelFixture? fixture, int budget = 256, bool suppress = false, float cacheDistance = 100, float emissionBoost = 1, VanillaGraphicsExpanded.Numerics.VectorInt3 worldOffset = default, int cacheResolution = 1,
        bool directionalCache = false, float anchorX = 0, float screenDepth = 1, bool nearFieldTracing = true, float screenEmission = 0, bool worldCache = true, VanillaGraphicsExpanded.WorldPartition.PartitionBounds? supportedOrigins = null, float maximumTraceReach = 0, float cacheSpacing = 8, VanillaGraphicsExpanded.LumOn.Scene.Geometry.TraceGeometryGpuScene? shared = null, VanillaGraphicsExpanded.LumOn.Scene.SurfaceLightingSnapshot? surfaceLighting = null, Vector3? anchorPosition = null, VanillaGraphicsExpanded.Numerics.Vector3d matrixRemainder = default, Action<GpuFramebuffer>? consume = null, VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu.LumOnWorldProbeClipmapGpuResources? worldResources = null, GpuTexture? history = null, GpuTexture? historyMeta = null, int texelsPerFrame = 64, int frameIndex = 0, float rayMaxDistance = 4, ShaderLightingResources? resources = null)
    {
        var key = new TraceProgramKey(nearFieldTracing, emissionBoost, texelsPerFrame, worldCache,
            cacheResolution, cacheSpacing, worldResources?.WorldProbeTileSize ?? 16, rayMaxDistance);
        if (!tracePrograms.TryGetValue(key, out var program))
        {
            program = Programs.Create<LumOnScreenProbeAtlasTraceShaderProgram>(shader =>
            {
                shader.NearField = key.NearField; shader.EmissiveBoost = key.EmissiveBoost;
                shader.TexelsPerFrame = key.TexelsPerFrame; shader.WorldProbes = key.WorldProbes;
                shader.WorldProbeLevels = 1; shader.WorldProbeResolution = key.WorldResolution;
                shader.WorldProbeBaseSpacing = key.WorldSpacing; shader.WorldProbeOctahedralSize = key.WorldTileSize;
                shader.HzbCoarseMip = 0; shader.RayMaxDistance = key.RayMaxDistance;
            });
            tracePrograms.Add(key, program);
        }
        using var use = program.UseScope();
        using var cacheBinding=new VanillaGraphicsExpanded.LumOn.Scene.SurfaceLightingBindings();
        cacheBinding.Bind(surfaceLighting);
        // A retained history supplies its own branch. Otherwise reuse this test's sequential component inputs.
        resources ??= LightingResources;
        var buffers = resources.EnsureScreen(4, 4, 2);
        resources.Scene.EnsureSize(4, 4);
        var terrain = resources.Scene.Engine;
        var gbuffer = resources.Scene.Terrain;
        // External world resources are borrowed, with no redundant fallback atlas allocation.
        var worldInputs = worldResources ?? resources.EnsureWorldProbes(cacheResolution, 1, 16);
        gbuffer.Material.UploadDataImmediate(CreateUniformColorData(4,4,0,0,screenEmission,0));
        program.ProbeAnchorPosition = Populate(buffers.ProbeAnchorPositionTex!, anchorPosition?.X ?? anchorX, anchorPosition?.Y ?? 0, anchorPosition?.Z ?? -5, 1);
        program.ProbeAnchorNormal = Populate(buffers.ProbeAnchorNormalTex!, .5f, .5f, 1, 0);
        program.PrimaryDepth = Populate(terrain.Depth, screenDepth).TextureId;
        program.SurfaceAlbedo = Populate(buffers.SurfaceAlbedoTex!, 1, 1, 1, 1);
        program.GBufferMaterial = gbuffer.Material.TextureId;
        program.ScreenProbeAtlasHistory = history ?? Populate(buffers.ScreenProbeAtlasHistoryTex!, 0, 0, 0, 0);
        program.ScreenProbeAtlasMetaHistory = historyMeta ?? Populate(buffers.ScreenProbeAtlasMetaHistoryTex!, 0, 0);
        program.HzbDepth = Populate(buffers.HzbDepthTex!, screenDepth);
        program.WorldProbeRadianceAtlas = worldResources?.ProbeRadianceAtlas ??
            Populate(worldInputs.ProbeRadianceAtlas, 10, 10, 10, MathF.Log(1 + cacheDistance));
        program.ProbeTraceMask = Populate(buffers.ProbeTraceMaskTex!, 0, 0);
        program.WorldProbeVis0 = worldResources?.ProbeVis0 ?? Populate(worldInputs.ProbeVis0, 0, 0, 1, 1);
        program.WorldProbeMeta0 = worldResources?.ProbeMeta0 ?? Populate(worldInputs.ProbeMeta0, 1, 0);
        var traceSettings = shared is null
            ? new LumOnNearFieldTraceSettings(budget, supportedOrigins, maximumTraceReach)
            : new LumOnNearFieldTraceSettings(budget, shared.Coverage?.NearField, float.MaxValue);
        program.BindNearFieldScene(shared ?? fixture?.Scene.Backend, traceSettings);
        UpdateAndBindLumOnFrameUbo(program, frameIndex: frameIndex, invProjectionMatrix: LumOnTestInputFactory.CreateRealisticInverseProjection(),
            projectionMatrix: LumOnTestInputFactory.CreateRealisticProjection(),
            matrixSpaceWorldChunkCoordOffset: new VanillaGraphicsExpanded.Numerics.VectorInt3(worldOffset.X >> 5, worldOffset.Y >> 5, worldOffset.Z >> 5), matrixSpaceWorldBlockOffsetRem: matrixRemainder);
        UpdateAndBindLumOnWorldProbeUbo(program, skyTint: new Vintagestory.API.MathTools.Vec3f(1, 1, 1), cameraPosWS: Vector3.Zero, originMinCorner: [new Vector3(-cacheSpacing * .5f * cacheResolution, -cacheSpacing * .5f * cacheResolution, (worldResources != null ? -3 : -5) - cacheSpacing * .5f * cacheResolution)]);

        program.SuppressWorldProbeRadiance = suppress;
        program.IndirectTint = new(1,1,1);
        var output = buffers.ScreenProbeAtlasTraceFbo!;
        TestFramework.RenderQuadTo(program, output);
        var result = (output[0].ReadPixels(), output[1].ReadPixels());
        consume?.Invoke(output);
        return result;

        /// <summary>Authors controlled values in a production-owned attachment, without choosing sampler slots.</summary>
        DynamicTexture2D Populate(DynamicTexture2D texture, params float[] value)
        {
            int width=texture.Width, height=texture.Height;
            var data = new float[width * height * value.Length];
            for (int i = 0; i < data.Length; i++) data[i] = value[i % value.Length];
            if (directionalCache && ReferenceEquals(texture, worldInputs.ProbeRadianceAtlas))
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    data[(y * width + x) * 4] = (x % 16 + 0.5f) / 16;
            texture.UploadDataImmediate(data);
            return texture;
        }
    }

    /// <summary>Decodes metadata without numeric conversion of the packed flag bits.</summary>
    protected static uint Flags(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value));
    #endregion
}
