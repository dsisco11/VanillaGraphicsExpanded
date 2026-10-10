using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.Client;
using Vintagestory.API.MathTools;
namespace VanillaGraphicsExpanded.Tests.GPU;
/// <summary>Exercises independent shared-depth publication using real registered renderer callbacks and native quality controls.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class DepthHierarchyRendererTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>AO-only publication reuses one chain, builds once per frame and retires on disable, reload and world teardown.</summary>
    [Fact]
    public void AoOnlyPublicationIsCurrentViewAndLifecycleBounded() {
        EnsureShaderTestAvailable();using var assets=new BinaryShaderApiFixture();using var platform=new EngineShaderPlatformScope();
        using var terrain=new EngineTerrainBuffers(7,3);terrain.Depth.UploadDataImmediate(Enumerable.Repeat(.4f,21).ToArray());
        var events=new RuntimeRenderEvents();var api=new Mock<ICoreClientAPI>{DefaultValue=DefaultValue.Mock};
        int quality=1;api.SetupGet(a=>a.Settings.Int["ssaoQuality"]).Returns(()=>quality);
        api.SetupGet(a=>a.Settings.Int["godRays"]).Returns(0);api.SetupGet(a=>a.Settings.Bool["bloom"]).Returns(false);api.SetupGet(a=>a.Settings.Bool["fxaa"]).Returns(false);
        api.SetupGet(a=>a.Assets).Returns(assets.Api.Assets);api.SetupGet(a=>a.Logger).Returns(assets.Api.Logger);
        api.SetupGet(a=>a.Shader).Returns(assets.Api.Shader);api.SetupGet(a=>a.Event).Returns(events.Api);
        float[] identity=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
        api.SetupGet(a=>a.Render.CurrentProjectionMatrix).Returns(identity);api.SetupGet(a=>a.Render.CameraMatrixOriginf).Returns(identity);
        api.SetupGet(a=>a.Render.FrameWidth).Returns(7);api.SetupGet(a=>a.Render.FrameHeight).Returns(3);
        api.SetupGet(a=>a.Render.ShaderUniforms).Returns(new DefaultShaderUniforms{ZNear=.1f,ZFar=100});
        api.SetupGet(a=>a.Render.FogColor).Returns(new Vec4f());
        api.SetupGet(a=>a.World.Player).Returns(RuntimeEngineServices.CameraPlayer(()=>new VanillaGraphicsExpanded.LumOn.LumOnCameraState(0,0,0,0,0,0,0)));
        var framebuffers=Enumerable.Repeat<FrameBufferRef>(null!,Enum.GetValues<EnumFrameBuffer>().Max(v=>(int)v)+1).ToList();
        framebuffers[(int)EnumFrameBuffer.Primary]=terrain.Primary;api.SetupGet(a=>a.Render.FrameBuffers).Returns(framebuffers);
        var previous=PbrShaderLightingMode.GenerationLumOnEnabled;PbrShaderLightingMode.GenerationLumOnEnabled=false;
        ScreenManager.Platform.DoPostProcessingEffects=true;
        using var frame=new VgeFrameRenderer(api.Object);using var owner=new DepthHierarchyRenderer(api.Object);
        try {
            Assert.True(VgeShaderPrograms.RegisterAll(api.Object));Assert.Equal(8.7,owner.RenderOrder);
            events.Render(EnumRenderStage.Before);Assert.Null(DepthHierarchyRenderer.Texture);
            events.Render(EnumRenderStage.Opaque);var first=DepthHierarchyRenderer.Texture!;Assert.NotNull(first);
            Assert.Same(VgeFrameRenderer.Current,DepthHierarchyRenderer.View);
            Assert.Equal(UboPacking.ReadUInt32(VgeFrameRenderer.Current.Bytes,396),DepthHierarchyRenderer.FrameIndex);
            for(int mip=0;mip<first.MipLevels;mip++)Assert.All(first.ReadPixels(mip),v=>Assert.Equal(.4f,v));
            terrain.Depth.UploadDataImmediate(Enumerable.Repeat(.7f,21).ToArray());events.Render(EnumRenderStage.Opaque);
            Assert.All(first.ReadPixels(0),v=>Assert.Equal(.4f,v));
            events.Render(EnumRenderStage.Before);Assert.Null(DepthHierarchyRenderer.Texture);
            events.Render(EnumRenderStage.Opaque);Assert.Same(first,DepthHierarchyRenderer.Texture);
            Assert.All(first.ReadPixels(0),v=>Assert.Equal(.7f,v));
            quality=0;events.Render(EnumRenderStage.Before);events.Render(EnumRenderStage.Opaque);
            Assert.Null(DepthHierarchyRenderer.Texture);Assert.False(first.IsValid);
            quality=2;events.Render(EnumRenderStage.Before);events.Render(EnumRenderStage.Opaque);
            var second=DepthHierarchyRenderer.Texture!;Assert.NotSame(first,second);
            events.ReloadShaders();Assert.Null(DepthHierarchyRenderer.Texture);Assert.False(second.IsValid);
            events.Render(EnumRenderStage.Before);events.Render(EnumRenderStage.Opaque);
            var third=DepthHierarchyRenderer.Texture!;
            // Exercise the same publication boundary used after engine attachment replacement.
            using var resized=new EngineTerrainBuffers(9,5);
            resized.Depth.UploadDataImmediate(Enumerable.Repeat(.2f,45).ToArray());
            framebuffers[(int)EnumFrameBuffer.Primary]=resized.Primary;
            api.SetupGet(a=>a.Render.FrameWidth).Returns(9);api.SetupGet(a=>a.Render.FrameHeight).Returns(5);
            ScreenResourceManager.HandleScreenResize();Assert.Null(DepthHierarchyRenderer.Texture);Assert.False(third.IsValid);
            events.Render(EnumRenderStage.Before);events.Render(EnumRenderStage.Opaque);
            var fourth=DepthHierarchyRenderer.Texture!;Assert.Equal(9,fourth.Width);Assert.Equal(5,fourth.Height);
            Assert.NotSame(third,fourth);
            for(int mip=0;mip<fourth.MipLevels;mip++)Assert.All(fourth.ReadPixels(mip),v=>Assert.Equal(.2f,v));
            events.LeaveWorld();Assert.Null(DepthHierarchyRenderer.Texture);Assert.False(fourth.IsValid);
            owner.Dispose();Assert.DoesNotContain(events.Registrations,r=>ReferenceEquals(r.Renderer,owner));
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        } finally {GpuShaderPrograms.Dispose(api.Object);PbrShaderLightingMode.GenerationLumOnEnabled=previous;}
    }
    #endregion
}
