using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Production shader bindings shared by the local-trace scenarios.</summary>
public abstract class LocalTraceShaderTestBase : LumOnShaderFunctionalTestBase
{
    /// <summary>Uses the shared headless GPU context.</summary>
    protected LocalTraceShaderTestBase(HeadlessGLFixture fixture) : base(fixture) { }

    #region Shader Harness
    /// <summary>Runs the production shader with the production sixteen-unit texture layout.</summary>
    private protected (float[] Radiance, float[] Meta) Trace(LocalTraceVoxelFixture fixture, int budget = 256, bool suppress = false, float cacheDistance = 100, float emissionBoost = 1, VanillaGraphicsExpanded.Numerics.VectorInt3 worldOffset = default, int cacheResolution = 1,
        bool directionalCache = false, float anchorX = 0, float screenDepth = 1, bool localTracing = true, float screenEmission = 0, bool worldCache = true)
    {
        int program = CompileShaderWithDefines("lumon_probe_atlas_trace.vsh", "lumon_probe_atlas_trace.fsh",
            new Dictionary<string, string?>
            {
                ["VGE_LUMON_LOCAL_TRACE_ENABLED"] = localTracing ? "1" : "0",
                ["LUMON_EMISSIVE_BOOST"] = emissionBoost.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = "64",
                ["VGE_LUMON_WORLDPROBE_ENABLED"] = worldCache ? "1" : "0",
                ["VGE_LUMON_WORLDPROBE_LEVELS"] = "1",
                ["VGE_LUMON_WORLDPROBE_RESOLUTION"] = cacheResolution.ToString(),
                ["VGE_LUMON_WORLDPROBE_BASE_SPACING"] = "8.0",
                ["VGE_LUMON_WORLDPROBE_OCTAHEDRAL_SIZE"] = "16",
                ["VGE_LUMON_BIND_WORLDPROBE_RADIANCE_ATLAS"] = "1",
                ["VGE_LUMON_HZB_COARSE_MIP"] = "0"
            });
        var textures = new List<DynamicTexture2D>();
        try
        {
            AddTexture("probeAnchorPosition", 0, 2, 2, PixelInternalFormat.Rgba16f, anchorX, 0, -5, 1);
            AddTexture("probeAnchorNormal", 1, 2, 2, PixelInternalFormat.Rgba16f, 0.5f, 0.5f, 1, 0);
            AddTexture("primaryDepth", 2, 4, 4, PixelInternalFormat.R32f, screenDepth);
            AddTexture("surfaceAlbedo", 3, 4, 4, PixelInternalFormat.Rgba16f, 1, 1, 1, 1);
            AddTexture("gBufferMaterial", 4, 4, 4, PixelInternalFormat.Rgba16f, 0, 0, screenEmission, 0);
            AddTexture("octahedralHistory", 5, 16, 16, PixelInternalFormat.Rgba16f, 0, 0, 0, 0);
            AddTexture("hzbDepth", 6, 4, 4, PixelInternalFormat.R32f, screenDepth);
            AddTexture("probeAtlasMetaHistory", 7, 16, 16, PixelInternalFormat.Rg32f, 0, 0);
            AddTexture("worldProbeRadianceAtlas", 8, 16 * cacheResolution * cacheResolution, 16 * cacheResolution, PixelInternalFormat.Rgba16f, 10, 10, 10, MathF.Log(1 + cacheDistance));
            AddTexture("probeTraceMask", 9, 2, 2, PixelInternalFormat.Rg32f, 0, 0);
            AddTexture("worldProbeVis0", 11, cacheResolution * cacheResolution, cacheResolution, PixelInternalFormat.Rgba16f, 0, 0, 1, 1);
            AddTexture("worldProbeMeta0", 12, cacheResolution * cacheResolution, cacheResolution, PixelInternalFormat.Rg32f, 1, 0);
            fixture.Scene.Geometry.Bind(10); fixture.Scene.Light.Bind(13);
            fixture.Scene.Regions.Bind(14); fixture.Scene.Materials.Bind(15);
            GL.UseProgram(program);
            GL.Uniform1(GL.GetUniformLocation(program, "localTraceGeometry"), 10);
            GL.Uniform1(GL.GetUniformLocation(program, "localTraceLight"), 13);
            GL.Uniform1(GL.GetUniformLocation(program, "localTraceRegions"), 14);
            GL.Uniform1(GL.GetUniformLocation(program, "localTraceMaterials"), 15);
            GL.UseProgram(0);
            UpdateAndBindLumOnFrameUbo(program, invProjectionMatrix: LumOnTestInputFactory.CreateRealisticInverseProjection(),
                projectionMatrix: LumOnTestInputFactory.CreateRealisticProjection(),
                matrixSpaceWorldChunkCoordOffset: new VanillaGraphicsExpanded.Numerics.VectorInt3(worldOffset.X >> 5, worldOffset.Y >> 5, worldOffset.Z >> 5));
            UpdateAndBindLumOnWorldProbeUbo(program, skyTint: new Vintagestory.API.MathTools.Vec3f(1, 1, 1), cameraPosWS: Vector3.Zero, originMinCorner: [new Vector3(-4 * cacheResolution, -4 * cacheResolution, -5 - 4 * cacheResolution)]);
            using var localBuffer = GpuUniformBuffer.Create(debugName: "Tests.LocalTrace");
            var local = new LumOnLocalTraceParamsUbo();
            local.Set(fixture.Scene.Origin, fixture.Scene.Resolution, budget);
            localBuffer.UploadOrResize(local.Bytes, growExponentially: false);
            localBuffer.BindBase(LumOnLocalTraceParamsUbo.Binding);
            UniformBlockBindingUtil.EnsureBlockBound(program, LumOnLocalTraceParamsUbo.BlockName, LumOnLocalTraceParamsUbo.Binding);
            using var parameters = new ObjectParamsUbo("Tests.LocalTrace.Params");
            parameters.UploadAndBind(new LumOnProbeParamsUbo { SuppressWorldProbeRadiance = suppress, IndirectTint = Vector3.One }.Bytes);
            UniformBlockBindingUtil.EnsureBlockBound(program, LumOnProbeParamsUbo.BlockName, GpuBindingRegistry.Ubo.Object);
            using var output = TestFramework.CreateTestGBuffer(16, 16, PixelInternalFormat.Rgba16f, PixelInternalFormat.Rg32f);
            TestFramework.RenderQuadTo(program, output);
            return (output[0].ReadPixels(), output[1].ReadPixels());
        }
        finally
        {
            foreach (var texture in textures) texture.Dispose();
            GL.DeleteProgram(program);
        }

        /// <summary>Creates uniform inputs that isolate traversal from unrelated screen-buffer contents.</summary>
        void AddTexture(string name, int unit, int width, int height, PixelInternalFormat format, params float[] value)
        {
            var data = new float[width * height * value.Length];
            for (int i = 0; i < data.Length; i++) data[i] = value[i % value.Length];
            if (directionalCache && name == "worldProbeRadianceAtlas")
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    data[(y * width + x) * 4] = (x % 16 + 0.5f) / 16;
            var texture = TestFramework.CreateTexture(width, height, format, data);
            textures.Add(texture); texture.Bind(unit);
            GL.UseProgram(program);
            GL.Uniform1(GL.GetUniformLocation(program, name), unit);
            GL.UseProgram(0);
        }
    }

    /// <summary>Decodes metadata without numeric conversion of the packed flag bits.</summary>
    protected static uint Flags(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value));
    #endregion
}
