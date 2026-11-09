using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Checks known surface-cache lighting through the composed debug shader component.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceCacheDebugBindingTests : LumOnShaderFunctionalTestBase
{
    #region Public API
    /// <summary>Uses the shared rendering context.</summary>
    public SurfaceCacheDebugBindingTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Irradiance rendering
    /// <summary>Known page-table and irradiance array inputs produce the expected tonemapped surface-cache pixel.</summary>
    [Fact]
    public void IrradianceViewDrawsKnownArrayLighting()
    {
        // Arrange: Author one ready page and known persistent irradiance in the exact debug shader input layout.
        EnsureShaderTestAvailable();
        var program = Programs.Create<LumOnDebugShaderProgram>(identity: LumOnDebugShaderProgramFamily.GetProgramName(LumOnDebugMode.LumonSceneIrradiance));

        {
            using var patch = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            patch.UploadDataImmediate(new uint[] { 0, 1, 0, 0 });
            using var depth = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            depth.UploadDataImmediate(new float[] { .5f });
            using var pages = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            pages.UploadDataImmediate(new uint[] { 0, 1u | (1u << 24) }, 0, 0, 0, 2, 1, 1);
            using var irradiance = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            // Persistent RGB is encoded; readiness alpha is not a physical-light quantity.
            const float storage = LightingStorage.PersistentScale;
            irradiance.UploadDataImmediate(new float[] { storage, 3 * storage, 7 * storage, 1 }, 0, 0, 0, 1, 1, 1);
            using var material = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            program.GBufferPatchId = patch;
            program.PrimaryDepth = depth;
            program.LumonScenePageTableMip0 = pages;
            program.LumonSceneIrradianceAtlas = irradiance;
            program.LumonSceneMaterialAtlas = material;
            program.LumonSceneEnabled = 1;
            program.LumonSceneTileSizeTexels = 1;
            program.LumonSceneTilesPerAxis = 1;
            program.LumonSceneTilesPerAtlas = 1;
            program.DebugMode = (int)LumOnDebugMode.LumonSceneIrradiance;
            UpdateAndBindLumOnFrameUbo(program);
            // Act: Render the composed debug shader into one floating-point output pixel.
            using var output = TestFramework.CreateTestGBuffer(1, 1, TextureFormat.Rgba32f);
            TestFramework.RenderQuadTo(program, output);
            float[] pixel = output[0].ReadPixels();
            // Assert: The pixel matches the shader's known tonemapped RGB and has no GL error.
            Assert.InRange(pixel[0], .499f, .501f);
            Assert.InRange(pixel[1], .749f, .751f);
            Assert.InRange(pixel[2], .874f, .876f);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }

    }
    #endregion
    #endregion
    #region Protected
    /// <summary>Prepares this suite's observation program before each test subject runs.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("layer", 0, 0);
    }
    #endregion
}
