using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;

/// <summary>Verifies native texture-array bindings made by the production surface-cache debug program.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceCacheDebugBindingTests : LumOnShaderFunctionalTestBase
{
    #region Public API
    /// <summary>Uses the shared rendering context.</summary>
    public SurfaceCacheDebugBindingTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Native array bindings
    /// <summary>Array-backed cache resources bind and clear their declared array target without OpenGL errors.</summary>
    [Theory]
    [InlineData(0, 26, TextureFormat.R32ui)]
    [InlineData(1, 27, TextureFormat.Rgba16f)]
    [InlineData(2, 28, TextureFormat.Rgba8)]
    public void SurfaceCacheSettersBindArrayTextures(int resource, int unit, TextureFormat format)
    {
        // Arrange: Create the declared array resource and a two-dimensional sentinel bound to the same unit.
        EnsureContextValid();
        var program = Programs.Create<LumOnDebugShaderProgram>(identity: LumOnDebugShaderProgramFamily.GetProgramName(resource == 2 ? LumOnDebugMode.LumonSceneMaterial : LumOnDebugMode.LumonSceneIrradiance));
        using var texture = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = format, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = format == TextureFormat.R32ui ? TextureFilterMode.Nearest : TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        using var sentinel = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        program.PrimaryDepth = sentinel;
        using var surfaceInput1 = LayeredTestTexture.Create(ImageAssertions.Programs, sentinel, null, null);
        program.GBufferSurface = surfaceInput1;
        UpdateAndBindLumOnFrameUbo(program);
        VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(unit, sentinel, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)sentinel).Target)))]);
        // Act: Bind the array through the production setter, then clear it through that same property.
        Set(texture);
        using var active = ShaderBindingTestPipeline.Create(program);
        RHICommandContext.Current.SetPipeline(active);
        RHICommandContext.Current.BindResources(program.CaptureBindings());
        // Assert: OpenGL reports no binding error and each texture target retains its independent binding.
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        OpenGLImageBindings.ActiveTexture(OpenGlStateCache.Current, unit);
        Assert.Equal(texture.TextureId, GL.GetInteger(GetPName.TextureBinding2DArray));
        Assert.Equal(sentinel.TextureId, GL.GetInteger(GetPName.TextureBinding2D));
        Set(null);
        RHICommandContext.Current.BindResources(program.CaptureBindings());
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        OpenGLImageBindings.ActiveTexture(OpenGlStateCache.Current, unit);
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
