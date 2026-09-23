using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks reuse, independent scene identity and borrowed-resource lifetime in shader fixtures.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ShaderSceneInputsTests : RenderTestBase
{
    /// <summary>Uses the shared GPU context.</summary>
    public ShaderSceneInputsTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Scene ownership
    /// <summary>Repeated population retains every allocation and cannot modify an equally sized independent scene.</summary>
    [Fact]
    public void PopulationReusesResourcesWithoutAliasingIndependentScenes()
    {
        EnsureContextValid();
        using var first = new ShaderSceneInputs();
        using var second = new ShaderSceneInputs();
        first.EnsureSize(2,2); second.EnsureSize(2,2);
        var original = Borrow(first);
        var output = first.Engine.Output;
        var independent = Borrow(second);
        second.Engine.Color.UploadDataImmediate(Enumerable.Repeat(.75f,16).ToArray());
        for (int frame=0;frame<3;frame++)
        {
            first.EnsureSize(2,2);
            Assert.Same(output,first.Engine.Output);
            first.Engine.Color.UploadDataImmediate(Enumerable.Repeat(frame*.25f,16).ToArray());
            first.Engine.Depth.UploadDataImmediate(Enumerable.Repeat(.5f,4).ToArray());
            first.Terrain.Normal.UploadDataImmediate(new float[16]);
            first.Terrain.Material.UploadDataImmediate(new float[16]);
            var current = Borrow(first);
            for (int i=0;i<original.Length;i++)
            {
                Assert.Same(original[i],current[i]);
                Assert.NotEqual(current[i].TextureId,independent[i].TextureId);
            }
        }
        Assert.All(first.Engine.Color.ReadPixels(), value => Assert.Equal(.5f,value));
        Assert.All(second.Engine.Color.ReadPixels(), value => Assert.Equal(.75f,value));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Resizing invalidates old borrows, preserves other scenes and leaves no live resources after disposal.</summary>
    [Fact]
    public void ResizeAndDisposalRetireOnlyTheOwningScene()
    {
        EnsureContextValid();
        using var scene = new ShaderSceneInputs();
        using var independent = new ShaderSceneInputs();
        scene.EnsureSize(2,2); independent.EnsureSize(2,2);
        var retired = Borrow(scene);
        var oldOutput = scene.Engine.Output;
        scene.EnsureSize(3,5);
        Assert.All(retired, texture => Assert.False(texture.IsValid));
        Assert.NotSame(oldOutput,scene.Engine.Output);
        var current = Borrow(scene);
        Assert.All(current, texture => Assert.Equal((3,5),(texture.Width,texture.Height)));
        scene.Dispose(); scene.Dispose();
        Assert.All(current, texture => Assert.False(texture.IsValid));
        Assert.All(Borrow(independent), texture => Assert.True(texture.IsValid));
        Assert.Throws<ObjectDisposedException>(() => scene.EnsureSize(2,2));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Observes all scene attachments without taking over their lifetime.</summary>
    private static GpuTexture[] Borrow(ShaderSceneInputs scene) =>
        [scene.Engine.Color,scene.Engine.Depth,scene.Terrain.Normal,scene.Terrain.Material,scene.Terrain.PatchId];
    #endregion
}
