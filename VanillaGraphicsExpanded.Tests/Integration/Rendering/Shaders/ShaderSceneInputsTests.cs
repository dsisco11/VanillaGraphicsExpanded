using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

/// <summary>Checks reuse, independent scene identity and borrowed-resource lifetime in shader fixtures.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShaderSceneInputsTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the shared GPU context.</summary>
    public ShaderSceneInputsTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Scene ownership
    /// <summary>Repeated population retains every allocation and cannot modify an equally sized independent scene.</summary>
    [Fact]
    public void PopulationReusesResourcesWithoutAliasingIndependentScenes()
    {
        // Arrange: Create equal-sized independent scenes and retain their allocation identities and initial peer pixels.
        EnsureContextValid();
        using var first = new ShaderSceneInputs();
        using var second = new ShaderSceneInputs();
        first.EnsureSize(2,2); second.EnsureSize(2,2);
        var original = Borrow(first);
        var output = first.Engine.Output;
        var independent = Borrow(second);
        second.Engine.Color.UploadDataImmediate(Enumerable.Repeat(.75f,16).ToArray());
        // Act: Repopulate one scene repeatedly with changing color/depth and terrain data.
        // Assert: Its allocations remain stable, scene IDs stay distinct, and each scene retains its own final pixels.
        for (int frame=0;frame<3;frame++)
        {
            first.EnsureSize(2,2);
            Assert.Same(output,first.Engine.Output);
            first.Engine.Color.UploadDataImmediate(Enumerable.Repeat(frame*.25f,16).ToArray());
            first.Engine.Depth.UploadDataImmediate(Enumerable.Repeat(.5f,4).ToArray());
            first.Terrain.Surface.UploadDataImmediate(new float[48], 0, 0, 0, 2, 2, 3);
            var current = Borrow(first);
            for (int i=0;i<original.Length;i++)
            {
                Assert.Same(original[i],current[i]);
                Assert.NotEqual(current[i].TextureId,independent[i].TextureId);
            }
        }
        ImageAssertions.Comparisons.CompareConstant(first.Engine.Color, .5f).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(second.Engine.Color, .75f).AssertEqual();
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Resizing invalidates old borrows, preserves other scenes and leaves no live resources after disposal.</summary>
    [Fact]
    public void ResizeAndDisposalRetireOnlyTheOwningScene()
    {
        // Arrange: Create two scenes and retain the first scene's borrowed image identities before resize.
        EnsureContextValid();
        using var scene = new ShaderSceneInputs();
        using var independent = new ShaderSceneInputs();
        scene.EnsureSize(2,2); independent.EnsureSize(2,2);
        var retired = Borrow(scene);
        var oldOutput = scene.Engine.Output;
        // Act: Resize and dispose only the first scene.
        // Assert: Its old resources retire, the peer remains live, and the disposed owner cannot be reused.
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
    #endregion
    #endregion

    #region Private
    /// <summary>Observes all scene attachments without taking over their lifetime.</summary>
    private static GpuTexture[] Borrow(ShaderSceneInputs scene) =>
        [scene.Engine.Color,scene.Engine.Depth,scene.Terrain.Surface,scene.Terrain.PatchId];
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
    #endregion
}
