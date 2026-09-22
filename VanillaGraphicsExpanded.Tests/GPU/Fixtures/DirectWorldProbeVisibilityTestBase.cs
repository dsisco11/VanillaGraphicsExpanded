using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene.NearField;
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
    // ShaderTestHelper owns these programs until fixture disposal. Multi-frame scenarios
    // reuse the same compiled variant, matching runtime behavior instead of relinking each draw.
    private readonly Dictionary<string, int> visibilityPrograms = new();
    /// <summary>Uses the shared mandatory GPU fixture.</summary>
    protected DirectWorldProbeVisibilityTestBase(HeadlessGLFixture fixture) : base(fixture) { }

    #region Direct Consumer Harness
    /// <summary>Renders debug modes, atlas gather (-1), or SH9 gather (-2), forcing invalid screen probes.</summary>
    private protected float[] RenderDirectVisibility(WorldProbeAtlasData atlas, NearFieldGpuScene? scene,
        Vector3 sampleCenter, Vector3 cacheOrigin, float spacing, int consumer,
        int size = 4, float span = 0.1f, int budget = 256, VectorInt3 worldOffset = default,
        Vector3 ring = default, bool suppress = false,
        Vector3[]? levelOrigins = null, Vector3[]? levelRings = null,
        Vector3d? playerOrigin = null, float cameraBob = 0,
        VanillaGraphicsExpanded.LumOn.Scene.Geometry.TraceGeometryGpuScene? shared = null)
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
        if (!visibilityPrograms.TryGetValue(key, out int program))
        {
            program = CompileShaderWithDefines(debug ? "lumon_debug.vsh" : shader + ".vsh", shader + ".fsh", defines);
            visibilityPrograms.Add(key, program);
        }
        var textures = new List<DynamicTexture2D>();
        try
        {
            // Match production sampler units, including SH9's sixteen-unit limit.
            // Gather uses integer full-resolution guide fetches; every addressed texel
            // must exist rather than relying on undefined out-of-range sampling.
            int guideSize = debug ? size : size * 2;
            var depth = new float[guideSize * guideSize];
            Array.Fill(depth, 0.5f);
            var normals = new float[depth.Length * 4];
            for (int i = 0; i < normals.Length; i += 4)
            {
                normals[i] = normals[i + 1] = 0.5f;
                normals[i + 3] = 1;
            }
            Add("primaryDepth", debug ? 0 : sh9 ? 9 : 3, guideSize, guideSize, PixelInternalFormat.R32f, depth);
            Add("gBufferNormal", debug ? 1 : sh9 ? 10 : 4, guideSize, guideSize, PixelInternalFormat.Rgba16f, normals);
            Add("worldProbeRadianceAtlas", debug ? 19 : sh9 ? 11 : 5, atlas.Width, atlas.Height, PixelInternalFormat.Rgba16f, atlas.Radiance);
            Add("worldProbeVis0", debug ? 22 : sh9 ? 14 : 8, atlas.ScalarWidth, atlas.ScalarHeight, PixelInternalFormat.Rgba16f, atlas.Visibility);
            Add("worldProbeMeta0", debug ? 24 : sh9 ? 15 : 9, atlas.ScalarWidth, atlas.ScalarHeight, PixelInternalFormat.Rg32f, atlas.Metadata);
            if (!debug)
            {
                Add("probeAnchorPosition", sh9 ? 7 : 1, 2, 2, PixelInternalFormat.Rgba16f, new float[16]);
                Add("probeAnchorNormal", sh9 ? 8 : 2, 2, 2, PixelInternalFormat.Rgba16f, new float[16]);
                if (sh9)
                    for (int i = 0; i < 7; i++) Add("probeSh" + i, i, 2, 2, PixelInternalFormat.Rgba16f, new float[16]);
                else Add("octahedralAtlas", 0, 16, 16, PixelInternalFormat.Rgba16f, new float[1024]);
            }
            int geometryUnit = debug ? 34 : sh9 ? 12 : 6;
            int readinessUnit = debug ? 35 : sh9 ? 13 : 7;
            (shared?.Geometry ?? scene?.Geometry)?.Bind(geometryUnit);
            (shared?.Readiness ?? scene?.Regions)?.Bind(readinessUnit);
            GL.UseProgram(program);
            GL.Uniform1(GL.GetUniformLocation(program, "nearFieldGeometry"), geometryUnit);
            GL.Uniform1(GL.GetUniformLocation(program, "nearFieldRegions"), readinessUnit);
            GL.UseProgram(0);
            using var localBuffer = GpuUniformBuffer.Create(debugName: "Tests.DirectVisibility");
            var local = new LumOnNearFieldParamsUbo();
            if (shared != null) local.SetShared(shared, budget);
            else local.Set(scene?.Origin ?? default, scene?.Resolution ?? 0, budget, scene?.CellSize ?? 16);
            localBuffer.UploadOrResize(local.Bytes, growExponentially: false);
            localBuffer.BindBase(LumOnNearFieldParamsUbo.Binding);
            UniformBlockBindingUtil.EnsureBlockBound(program, LumOnNearFieldParamsUbo.BlockName, LumOnNearFieldParamsUbo.Binding);

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
            using var parameters = new ObjectParamsUbo("Tests.DirectVisibility.Params");
            if (debug)
            {
                parameters.UploadAndBind(new LumOnDebugParamsUbo { DebugMode = consumer }.Bytes);
                UniformBlockBindingUtil.EnsureBlockBound(program, LumOnDebugParamsUbo.BlockName, GpuBindingRegistry.Ubo.Object);
            }
            else
            {
                parameters.UploadAndBind(new LumOnProbeParamsUbo
                {
                    Intensity = 1, IndirectTint = Vector3.One, SampleStride = 1,
                    SuppressWorldProbeRadiance = suppress
                }.Bytes);
                UniformBlockBindingUtil.EnsureBlockBound(program, LumOnProbeParamsUbo.BlockName, GpuBindingRegistry.Ubo.Object);
            }
            using var output = TestFramework.CreateTestGBuffer(size, size, PixelInternalFormat.Rgba16f);
            TestFramework.RenderQuadTo(program, output);
            return output[0].ReadPixels();
        }
        finally
        {
            foreach (var texture in textures) texture.Dispose();
        }

        /// <summary>Creates and binds an explicitly packed input texture.</summary>
        void Add(string name, int unit, int width, int height, PixelInternalFormat format, float[] data)
        {
            var texture = TestFramework.CreateTexture(width, height, format, data);
            textures.Add(texture);
            texture.Bind(unit);
            GL.UseProgram(program);
            GL.Uniform1(GL.GetUniformLocation(program, name), unit);
            GL.UseProgram(0);
        }
    }
    #endregion
}
