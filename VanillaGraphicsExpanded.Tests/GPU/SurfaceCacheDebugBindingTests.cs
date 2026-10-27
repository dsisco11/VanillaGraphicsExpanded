using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
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
    [InlineData(0, 26, PixelInternalFormat.R32ui)]
    [InlineData(1, 27, PixelInternalFormat.Rgba16f)]
    [InlineData(2, 28, PixelInternalFormat.Rgba8)]
    public void SurfaceCacheSettersBindArrayTextures(int resource, int unit, PixelInternalFormat format)
    {
        EnsureContextValid();
        var program = Programs.Create<LumOnDebugShaderProgram>(identity: LumOnDebugShaderProgramFamily.GetProgramName(resource == 2 ? LumOnDebugMode.LumonSceneMaterial : LumOnDebugMode.LumonSceneIrradiance));
        using var texture = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Format((OpenTK.Graphics.OpenGL.PixelInternalFormat)(format)), Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        using var sentinel = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        program.PrimaryDepth = sentinel.TextureId;
        using var surfaceInput1 = LayeredTestTexture.Create(sentinel, null, null);
        program.GBufferSurface = surfaceInput1;
        UpdateAndBindLumOnFrameUbo(program);
        sentinel.Bind(unit);
        Set(texture);
        using var active = program.UseScope();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        OpenGLImageBindings.ActiveTexture(StateCache.Current, unit);
        Assert.Equal(texture.TextureId, GL.GetInteger(GetPName.TextureBinding2DArray));
        Assert.Equal(sentinel.TextureId, GL.GetInteger(GetPName.TextureBinding2D));
        Set(null);
        program.Use();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        OpenGLImageBindings.ActiveTexture(StateCache.Current, unit);
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
        var program = Programs.Create<LumOnDebugShaderProgram>(identity: LumOnDebugShaderProgramFamily.GetProgramName(LumOnDebugMode.LumonSceneIrradiance));

        {
            using var patch = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            patch.UploadDataImmediate(new uint[] { 0, 1, 0, 0 });
            using var depth = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            depth.UploadDataImmediate(new float[] { .5f });
            using var pages = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            pages.UploadDataImmediate(new uint[] { 0, 1u | (1u << 24) }, 0, 0, 0, 2, 1, 1);
            using var irradiance = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            irradiance.UploadDataImmediate(new float[] { 1, 3, 7, 1 }, 0, 0, 0, 1, 1, 1);
            using var material = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
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
