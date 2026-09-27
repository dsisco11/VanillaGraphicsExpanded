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
        Assert.False(store.TryGetDisplacementTextures(7,out _));
        store.SyncToAtlasPages([(7,4,4)],true);
        Assert.True(store.TryGetDisplacementTextures(7,out var texture));
        var pixels=texture!.Indices.ReadPixels();
        Assert.Equal(new float[] {.25f,.25f,.5f,.5f,.03f,0,0,0}, texture.Records.ReadPixels());
        for(int y=0;y<4;y++) for(int x=0;x<4;x++)
            Assert.Equal(x is 1 or 2 && y is 1 or 2 ? 1f : 0f,pixels[y*4+x]);
        store.UpdateDisplacement(plan with {MaterialParamsTiles=[tile with {Definition=definition with {DisplacementAmplitudeMetres=0}}]});
        Assert.False(store.TryGetDisplacementTextures(7,out _));
        store.UpdateDisplacement(plan);
        Assert.True(store.TryGetDisplacementTextures(7,out _));
        store.SyncToAtlasPages([(7,8,8)],true);
        Assert.False(store.TryGetDisplacementTextures(7,out _));
        store.UpdateDisplacement(plan);
        Assert.False(store.TryGetDisplacementTextures(7,out _)); // Stale plan dimensions cannot publish.
        store.UpdateDisplacement(plan with {MaterialParamsTiles=[]});
        Assert.False(store.TryGetDisplacementTextures(7,out _));
    }
    #endregion

    #region Compact record addressing
    /// <summary>Tile indices remain exact across record rows and unassigned atlas pixels stay disabled.</summary>
    [Fact]
    public void RecordsCrossRowsWithoutRepeatingRectanglePerPixel()
    {
        EnsureContextValid();
        using var store = new MaterialAtlasTextureStore();
        var definition = new PbrMaterialDefinition(.5f,0,0,default,PbrOverrideScale.Identity,0,null,DisplacementAmplitudeMetres:.02f);
        var tiles = Enumerable.Range(0,129).Select(index => new AtlasBuildPlan.MaterialParamsTileJob(
            7,new AtlasRect(index,0,1,1),new AssetLocation("game",$"textures/block/{index}.png"),
            definition,PbrOverrideScale.Identity,0)).ToArray();
        var plan = new AtlasBuildPlan(new AtlasSnapshot([],[],0,0),[new(7,256,1)],tiles,[],[],[],default);
        store.SyncToAtlasPages([(7,256,1)],true);
        store.UpdateDisplacement(plan);
        Assert.True(store.TryGetDisplacementTextures(7,out var page));
        Assert.Equal(256,page!.Records.Width);
        Assert.Equal(2,page.Records.Height);
        var indices = page.Indices.ReadPixels();
        for (int index=0;index<256;index++) Assert.Equal(index<129 ? index+1 : 0,indices[index]);
        var records = page.Records.ReadPixels();
        int offset = 128 << 3;
        Assert.Equal(.5f,records[offset]);
        Assert.Equal(1f/256,records[offset+2]);
        Assert.Equal(.02f,records[offset+4]);
    }
    #endregion
}
