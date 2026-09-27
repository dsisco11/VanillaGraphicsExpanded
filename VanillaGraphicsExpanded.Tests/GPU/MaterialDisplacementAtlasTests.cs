using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks atlas amplitude publication and removal independently of baked height data.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class MaterialDisplacementAtlasTests : RenderTestBase
{
    /// <summary>Uses the shared headless context.</summary>
    public MaterialDisplacementAtlasTests(HeadlessGLFixture fixture):base(fixture) { }

    #region Metadata publication
    /// <summary>Only mapped rects carry amplitude; absent heights and rebuilt assignments cannot expose stale opt-ins.</summary>
    [Fact]
    public void RebuildPublishesRectAndClearsRemovedAmplitude()
    {
        EnsureContextValid();
        using var store=new MaterialAtlasTextureStore();
        var definition=new PbrMaterialDefinition(.5f,0,0,default,PbrOverrideScale.Identity,0,null,DisplacementAmplitudeMetres:.03f);
        var tile=new AtlasBuildPlan.MaterialParamsTileJob(7,new AtlasRect(1,1,2,2),new AssetLocation("game","textures/block/test.png"),definition,PbrOverrideScale.Identity,0);
        var plan=new AtlasBuildPlan(new AtlasSnapshot([],[],0,0),[new(7,4,4)],[tile],[],[],[],default);
        store.UpdateDisplacement(plan);
        Assert.False(store.TryGetDisplacementTexture(7,out _));
        store.SyncToAtlasPages([(7,4,4)],true);
        Assert.True(store.TryGetDisplacementTexture(7,out var texture));
        var pixels=new float[16];
        GL.BindTexture(TextureTarget.Texture2D,texture!.TextureId);
        GL.GetTexImage(TextureTarget.Texture2D,0,PixelFormat.Red,PixelType.Float,pixels);
        for(int y=0;y<4;y++) for(int x=0;x<4;x++)
            Assert.Equal(x is 1 or 2 && y is 1 or 2 ? .03f : 0f,pixels[y*4+x]);
        store.UpdateDisplacement(plan with {MaterialParamsTiles=[tile with {Definition=definition with {DisplacementAmplitudeMetres=0}}]});
        Assert.False(store.TryGetDisplacementTexture(7,out _));
        store.UpdateDisplacement(plan);
        Assert.True(store.TryGetDisplacementTexture(7,out _));
        store.SyncToAtlasPages([(7,8,8)],true);
        Assert.False(store.TryGetDisplacementTexture(7,out _));
        store.UpdateDisplacement(plan with {MaterialParamsTiles=[]});
        Assert.False(store.TryGetDisplacementTexture(7,out _));
    }
    #endregion
}
