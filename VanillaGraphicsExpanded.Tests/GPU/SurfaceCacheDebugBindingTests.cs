using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises surface-cache texture binding through the actual debug shader owner.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SurfaceCacheDebugBindingTests : LumOnShaderFunctionalTestBase
{
    /// <summary>Uses the shared rendering context.</summary>
    public SurfaceCacheDebugBindingTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Production array setters
    /// <summary>Array-backed cache resources bind and clear their declared array target without OpenGL errors.</summary>
    [Theory]
    [InlineData(0, 30, PixelInternalFormat.R32ui)]
    [InlineData(1, 31, PixelInternalFormat.Rgba16f)]
    [InlineData(2, 32, PixelInternalFormat.Rgba8)]
    public void SurfaceCacheSettersBindArrayTextures(int resource, int unit, PixelInternalFormat format)
    {
        EnsureContextValid();
        var program = Programs.Create<LumOnDebugShaderProgram>(identity: "lumon_debug_gbuffer");
        using var active = program.UseScope();
        using var texture = Texture3D.Create(2, 2, 2, format, textureTarget: TextureTarget.Texture2DArray);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        using var sentinel = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba8);
        sentinel.Bind(unit);
        Set(texture);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        GlStateCache.Current.ActiveTexture(unit);
        Assert.Equal(texture.TextureId, GL.GetInteger(GetPName.TextureBinding2DArray));
        Assert.Equal(sentinel.TextureId, GL.GetInteger(GetPName.TextureBinding2D));
        Set(null);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        GlStateCache.Current.ActiveTexture(unit);
        Assert.Equal(0, GL.GetInteger(GetPName.TextureBinding2DArray));

        /// <summary>Calls the production property used by the debug renderer for each cache resource.</summary>
        void Set(GpuTexture? value)
        {
            switch (resource)
            {
                case 0: program.LumonScenePageTableMip0 = value; break;
                case 1: program.LumonSceneIrradianceAtlas = value; break;
                case 2: program.LumonSceneMaterialAtlas = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(resource));
            }
        }
    }
    /// <summary>Known page-table and irradiance array inputs produce the expected tonemapped surface-cache pixel.</summary>
    [Fact]
    public void IrradianceViewDrawsKnownArrayLighting()
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<LumOnDebugShaderProgram>(identity: "lumon_debug_gbuffer");
        using var active = program.UseScope();

        {
            using var patch = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32ui);
            patch.UploadDataImmediate(new uint[] { 0, 1, 0, 0 });
            using var depth = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f);
            depth.UploadDataImmediate(new float[] { .5f });
            using var pages = Texture3D.Create(2, 1, 1, PixelInternalFormat.R32ui, textureTarget: TextureTarget.Texture2DArray);
            pages.UploadDataImmediate(new uint[] { 0, 1u | (1u << 24) }, 0, 0, 0, 2, 1, 1);
            using var irradiance = Texture3D.Create(1, 1, 1, PixelInternalFormat.Rgba16f, textureTarget: TextureTarget.Texture2DArray);
            irradiance.UploadDataImmediate(new float[] { 1, 3, 7, 1 }, 0, 0, 0, 1, 1, 1);
            using var material = Texture3D.Create(1, 1, 1, PixelInternalFormat.Rgba8, textureTarget: TextureTarget.Texture2DArray);
            program.GBufferPatchId = patch.TextureId;
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
            using var output = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
            TestFramework.RenderQuadTo(program, output);
            float[] pixel = output[0].ReadPixels();
            Assert.InRange(pixel[0], .499f, .501f);
            Assert.InRange(pixel[1], .749f, .751f);
            Assert.InRange(pixel[2], .874f, .876f);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }

    }
    #endregion
}
