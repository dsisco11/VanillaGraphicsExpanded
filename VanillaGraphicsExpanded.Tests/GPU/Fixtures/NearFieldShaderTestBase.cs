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
    /// <summary>Uses the shared headless GPU context.</summary>
    protected NearFieldShaderTestBase(HeadlessGLFixture fixture) : base(fixture) { }

    #region Shader Harness
    /// <summary>Runs the production shader with the production sixteen-unit texture layout.</summary>
    private protected (float[] Radiance, float[] Meta) Trace(NearFieldVoxelFixture fixture, int budget = 256, bool suppress = false, float cacheDistance = 100, float emissionBoost = 1, VanillaGraphicsExpanded.Numerics.VectorInt3 worldOffset = default, int cacheResolution = 1,
        bool directionalCache = false, float anchorX = 0, float screenDepth = 1, bool nearFieldTracing = true, float screenEmission = 0, bool worldCache = true, VanillaGraphicsExpanded.WorldPartition.PartitionBounds? supportedOrigins = null, float maximumTraceReach = 0, float cacheSpacing = 8, VanillaGraphicsExpanded.LumOn.Scene.Geometry.TraceGeometryGpuScene? shared = null, VanillaGraphicsExpanded.LumOn.Scene.SurfaceLightingSnapshot? surfaceLighting = null, Vector3? anchorPosition = null, VanillaGraphicsExpanded.Numerics.Vector3d matrixRemainder = default, Action<GpuFramebuffer>? consume = null, VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu.LumOnWorldProbeClipmapGpuResources? worldResources = null, GpuTexture? history = null, GpuTexture? historyMeta = null, int texelsPerFrame = 64, int frameIndex = 0, float rayMaxDistance = 4)
    {
        int program = CompileShaderWithDefines("lumon_probe_atlas_trace.vsh", "lumon_probe_atlas_trace.fsh",
            new Dictionary<string, string?>
            {
                ["VGE_LUMON_NEAR_FIELD_ENABLED"] = nearFieldTracing ? "1" : "0",
                ["LUMON_EMISSIVE_BOOST"] = emissionBoost.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = texelsPerFrame.ToString(),
                ["VGE_LUMON_WORLDPROBE_ENABLED"] = worldCache ? "1" : "0",
                ["VGE_LUMON_WORLDPROBE_LEVELS"] = "1",
                ["VGE_LUMON_WORLDPROBE_RESOLUTION"] = cacheResolution.ToString(),
                ["VGE_LUMON_WORLDPROBE_BASE_SPACING"] = cacheSpacing.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                ["VGE_LUMON_WORLDPROBE_OCTAHEDRAL_SIZE"] = (worldResources?.WorldProbeTileSize ?? 16).ToString(),
                ["VGE_LUMON_HZB_COARSE_MIP"] = "0",
                ["VGE_LUMON_RAY_MAX_DISTANCE"] = rayMaxDistance.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
            });
        using var cacheBinding=new VanillaGraphicsExpanded.LumOn.Scene.SurfaceLightingBindings();
        cacheBinding.Bind(surfaceLighting);
        using var assets = new BinaryShaderApiFixture();
        var config = new VgeConfig();
        config.LumOn.ProbeSpacingPx = 2;
        using var buffers = new LumOnBufferManager(assets.Api, config);
        buffers.EnsureBuffers(4,4);
        using var worldInputs = new LumOnWorldProbeClipmapGpuResources(cacheResolution,1,16);
        using var terrain = new EngineTerrainBuffers(4,4);
        using var gbuffer = new GBufferTextures(4,4);
        gbuffer.Material.UploadDataImmediate(CreateUniformColorData(4,4,0,0,screenEmission,0));
        try
        {
            AddTexture("probeAnchorPosition", 0, buffers.ProbeAnchorPositionTex!, anchorPosition?.X ?? anchorX, anchorPosition?.Y ?? 0, anchorPosition?.Z ?? -5, 1);
            AddTexture("probeAnchorNormal", 1, buffers.ProbeAnchorNormalTex!, 0.5f, 0.5f, 1, 0);
            AddTexture("primaryDepth", 2, terrain.Depth, screenDepth);
            AddTexture("surfaceAlbedo", 3, buffers.SurfaceAlbedoTex!, 1, 1, 1, 1);
            BindSampler("gBufferMaterial",4,gbuffer.Material.TextureId);
            AddTexture("octahedralHistory", 5, buffers.ScreenProbeAtlasHistoryTex!, 0, 0, 0, 0);
            AddTexture("hzbDepth", 6, buffers.HzbDepthTex!, screenDepth);
            AddTexture("probeAtlasMetaHistory", 7, buffers.ScreenProbeAtlasMetaHistoryTex!, 0, 0);
            AddTexture("worldProbeRadianceAtlas", 8, worldInputs.ProbeRadianceAtlas, 10, 10, 10, MathF.Log(1 + cacheDistance));
            AddTexture("probeTraceMask", 9, buffers.ProbeTraceMaskTex!, 0, 0);
            AddTexture("worldProbeVis0", 11, worldInputs.ProbeVis0, 0, 0, 1, 1);
            AddTexture("worldProbeMeta0", 12, worldInputs.ProbeMeta0, 1, 0);
            (shared?.Geometry ?? fixture.Scene.Geometry).Bind(10); (shared?.Light ?? fixture.Scene.Light).Bind(13);
            (shared?.Readiness ?? fixture.Scene.Regions).Bind(14); (shared?.Materials ?? fixture.Scene.Materials).Bind(15);
            if(shared != null) shared.Faces.Bind(19);
            GL.UseProgram(program);
            GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(program, "nearFieldGeometry"), 10);
            GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(program, "nearFieldLight"), 13);
            GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(program, "nearFieldRegions"), 14);
            GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(program, "nearFieldMaterials"), 15);
            foreach(var binding in new[]{("capturedMaterial",16),("previousOutgoing",17),("surfacePages",18),("traceSceneFaces",19)})
            {
                int location=global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(program,binding.Item1);
                if(location>=0) GL.Uniform1(location,binding.Item2);
            }
            GL.UseProgram(0);
            UpdateAndBindLumOnFrameUbo(program, frameIndex: frameIndex, invProjectionMatrix: LumOnTestInputFactory.CreateRealisticInverseProjection(),
                projectionMatrix: LumOnTestInputFactory.CreateRealisticProjection(),
                matrixSpaceWorldChunkCoordOffset: new VanillaGraphicsExpanded.Numerics.VectorInt3(worldOffset.X >> 5, worldOffset.Y >> 5, worldOffset.Z >> 5), matrixSpaceWorldBlockOffsetRem: matrixRemainder);
            UpdateAndBindLumOnWorldProbeUbo(program, skyTint: new Vintagestory.API.MathTools.Vec3f(1, 1, 1), cameraPosWS: Vector3.Zero, originMinCorner: [new Vector3(-cacheSpacing * .5f * cacheResolution, -cacheSpacing * .5f * cacheResolution, (worldResources != null ? -3 : -5) - cacheSpacing * .5f * cacheResolution)]);
            using var localBuffer = GpuUniformBuffer.Create(debugName: "Tests.NearField");
            var local = new LumOnNearFieldParamsUbo();
            if (shared != null) local.SetShared(shared, budget);
            else local.Set(fixture.Scene.Origin, fixture.Scene.Resolution, budget, fixture.Scene.CellSize, supportedOrigins, maximumTraceReach);
            localBuffer.UploadOrResize(local.Bytes, growExponentially: false);
            localBuffer.BindBase(LumOnNearFieldParamsUbo.Binding);
            UniformBlockBindingUtil.EnsureBlockBound(program, LumOnNearFieldParamsUbo.BlockName, LumOnNearFieldParamsUbo.Binding);
            using var parameters = new ObjectParamsUbo("Tests.NearField.Params");
            parameters.UploadAndBind(new LumOnProbeParamsUbo { SuppressWorldProbeRadiance = suppress, IndirectTint = Vector3.One }.Bytes);
            UniformBlockBindingUtil.EnsureBlockBound(program, LumOnProbeParamsUbo.BlockName, GpuBindingRegistry.Ubo.Object);
            var output = buffers.ScreenProbeAtlasTraceFbo!;
            TestFramework.RenderQuadTo(program, output);
            var result = (output[0].ReadPixels(), output[1].ReadPixels());
            consume?.Invoke(output);
            return result;
        }
        finally
        {
            global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteProgram(program);
        }

        /// <summary>Binds an engine-owned attachment without duplicating its allocation or storage format.</summary>
        void BindSampler(string name, int unit, int texture)
        {
            GlStateCache.Current.BindTexture(TextureTarget.Texture2D,unit,texture);
            GL.UseProgram(program); GL.Uniform1(TestShaderInterfaces.GetUniformLocation(program,name),unit); GL.UseProgram(0);
        }

        /// <summary>Populates owned inputs that isolate traversal from unrelated screen-buffer contents.</summary>
        void AddTexture(string name, int unit, DynamicTexture2D texture, params float[] value)
        {
            GpuTexture? published = name switch
            {
                "octahedralHistory" => history,
                "probeAtlasMetaHistory" => historyMeta,
                "worldProbeRadianceAtlas" => worldResources?.ProbeRadianceAtlas,
                "worldProbeVis0" => worldResources?.ProbeVis0,
                "worldProbeMeta0" => worldResources?.ProbeMeta0,
                _ => null
            };
            if (published is not null)
            {
                published.Bind(unit);
                GL.UseProgram(program);
                GL.Uniform1(TestShaderInterfaces.GetUniformLocation(program,name),unit);
                GL.UseProgram(0);
                return;
            }
            int width=texture.Width, height=texture.Height;
            var data = new float[width * height * value.Length];
            for (int i = 0; i < data.Length; i++) data[i] = value[i % value.Length];
            if (directionalCache && name == "worldProbeRadianceAtlas")
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    data[(y * width + x) * 4] = (x % 16 + 0.5f) / 16;
            texture.UploadDataImmediate(data); texture.Bind(unit);
            GL.UseProgram(program);
            GL.Uniform1(global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(program, name), unit);
            GL.UseProgram(0);
        }
    }

    /// <summary>Decodes metadata without numeric conversion of the packed flag bits.</summary>
    protected static uint Flags(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value));
    #endregion
}
