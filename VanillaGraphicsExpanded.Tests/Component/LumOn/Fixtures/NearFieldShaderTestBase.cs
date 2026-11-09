using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.Support.Lighting;
using VanillaGraphicsExpanded.Tests.Support.Numerics;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;

/// <summary>Production shader bindings shared by the near-field scenarios.</summary>
public abstract partial class NearFieldShaderTestBase : LumOnShaderFunctionalTestBase
{
    #region Scenario programs
    private readonly NearFieldNumericalProgramFixture? numericalPrograms;

    // Match the direct-visibility harness: reuse only within this test, with disposal owned by Programs.
    private readonly Dictionary<NearFieldTraceProgramKey, LumOnScreenProbeAtlasTraceShaderProgram> tracePrograms = new();
    #endregion

    /// <summary>Uses the shared headless GPU context.</summary>
    protected NearFieldShaderTestBase(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>Uses class-owned numerical executables while keeping every case's scene resources independent.</summary>
    /// <param name="fixture">The collection-owned headless graphics context.</param>
    /// <param name="numericalPrograms">The class fixture that leases compiled trace variants.</param>
    protected NearFieldShaderTestBase(HeadlessGLFixture fixture, NearFieldNumericalProgramFixture numericalPrograms) : base(fixture)
        => this.numericalPrograms = numericalPrograms ?? throw new ArgumentNullException(nameof(numericalPrograms));

    #region Shader Harness
    /// <summary>Runs the production shader with the production sixteen-unit texture layout.</summary>
    private protected TraceImages Trace(NearFieldVoxelFixture? fixture, int budget = 256, bool suppress = false, float cacheDistance = 100, float emissionBoost = 1, VanillaGraphicsExpanded.Numerics.VectorInt3 worldOffset = default, int cacheResolution = 1,
        bool directionalCache = false, float anchorX = 0, float screenDepth = 1, bool nearFieldTracing = true, float screenEmission = 0, bool worldCache = true, VanillaGraphicsExpanded.WorldPartition.PartitionBounds? supportedOrigins = null, float maximumTraceReach = 0, float cacheSpacing = 8, VanillaGraphicsExpanded.LumOn.Scene.Geometry.TraceGeometryGpuScene? shared = null, VanillaGraphicsExpanded.LumOn.Scene.SurfaceLightingSnapshot? surfaceLighting = null, Vector3? anchorPosition = null, VanillaGraphicsExpanded.Numerics.Vector3d matrixRemainder = default, Action<RHIRenderPassDescription>? consume = null, VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu.LumOnWorldProbeClipmapGpuResources? worldResources = null, GpuTexture? history = null, GpuTexture? historyMeta = null, int texelsPerFrame = 64, int frameIndex = 0, float rayMaxDistance = 4, ShaderLightingResources? resources = null)
    {
        var key = new NearFieldTraceProgramKey(nearFieldTracing, emissionBoost, texelsPerFrame, worldCache,
            cacheResolution, cacheSpacing, worldResources?.WorldProbeTileSize ?? 16, rayMaxDistance);
        using var lease = numericalPrograms?.Acquire(key);
        LumOnScreenProbeAtlasTraceShaderProgram program;
        if (lease is not null)
        {
            program = lease.Program;
        }
        else if (!tracePrograms.TryGetValue(key, out program!))
        {
            program = Programs.Create<LumOnScreenProbeAtlasTraceShaderProgram>(shader => key.Configure(shader));
            tracePrograms.Add(key, program);
        }
        program.SetSurfaceLighting(surfaceLighting);
        // A retained history supplies its own branch. Otherwise reuse this test's sequential component inputs.
        resources ??= LightingResources;
        var buffers = resources.EnsureScreen(4, 4, 2);
        resources.Scene.EnsureSize(4, 4);
        var terrain = resources.Scene.Engine;
        var gbuffer = resources.Scene.Terrain;
        // External world resources are borrowed, with no redundant fallback atlas allocation.
        var worldInputs = worldResources ?? resources.EnsureWorldProbes(cacheResolution, 1, 16);
        // The scenario supplies physical nits; the material channel stores a normalized coefficient.
        gbuffer.Surface.UploadDataImmediate(CreateUniformColorData(4,4,0,0,
            screenEmission / LightingCalibration.EmissionReferenceNits,0), 0, 0, 1, 4, 4, 1);
        var anchors = buffers.ProbeAnchors!;
        anchors.UploadDataImmediate(CreateUniformColorData(anchors.Width, anchors.Height, anchorPosition?.X ?? anchorX, anchorPosition?.Y ?? 0, anchorPosition?.Z ?? -5, 1), 0, 0, 0, anchors.Width, anchors.Height, 1);
        anchors.UploadDataImmediate(CreateUniformColorData(anchors.Width, anchors.Height, .5f, .5f, 1, 0), 0, 0, 1, anchors.Width, anchors.Height, 1);
        program.ProbeAnchors = anchors;
        program.PrimaryDepth = Populate(terrain.Depth, screenDepth);
        program.SurfaceAlbedo = Populate(buffers.SurfaceAlbedoTex!, 1, 1, 1, 1);
        program.GBufferSurface = gbuffer.Surface;
        program.ScreenProbeAtlasHistory = history ?? Populate(buffers.ScreenProbeAtlasHistoryTex!, 0, 0, 0, 0);
        program.ScreenProbeAtlasMetaHistory = historyMeta ?? Populate(buffers.ScreenProbeAtlasMetaHistoryTex!, 0, 0);
        program.HzbDepth = terrain.Depth;
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
        var output = buffers.ScreenProbeAtlasTracePass!;
        TestFramework.RenderQuadTo(program, output);
        // Capture before downstream temporal consumers can reuse any attachment.
        var result = CaptureTrace((GpuTexture)output.Colors[0].Image!.Value.Texture.Resource!,
            (GpuTexture)output.Colors[1].Image!.Value.Texture.Resource!);
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
            if (ReferenceEquals(texture, worldInputs.ProbeRadianceAtlas))
                for (int offset = 0; offset < data.Length; offset += 4)
                    for (int channel = 0; channel < 3; channel++)
                        data[offset + channel] *= LightingStorage.PersistentScale;
            texture.UploadDataImmediate(data);
            return texture;
        }
    }

    /// <summary>Decodes metadata without numeric conversion of the packed flag bits.</summary>
    protected static uint Flags(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value));
    #endregion
}
