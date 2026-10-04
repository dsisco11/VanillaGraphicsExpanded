using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes compiled receiver and SSAO shaders against uploaded, independently specified particle samples.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SceneColorParticleSsaoTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Capture and receiver separation preserve the underlying material metadata for deferred lighting.</summary>
    [Fact]
    public void UploadedParticleSamplesLeaveReceiverMetadataUnchangedThroughResolve()
    {
        EnsureShaderTestAvailable();
        using var scene = new ParticleSsaoCaptureFixture();
        float[] originalNormals = scene.Normal.ReadPixels();
        float[] originalPositions = scene.Position.ReadPixels();
        scene.CaptureUploadedSamples();
        Assert.True(scene.Targets.Captured);
        Assert.Equal(originalNormals, scene.Normal.ReadPixels());
        Assert.Equal(originalPositions, scene.Position.ReadPixels());

        scene.ResolveReceivers(Programs.Create<SceneColorParticleShaderProgram>(), TestFramework);

        Assert.Equal(new[] { .75f, .75f, .75f, .75f }, scene.Targets.ResolveTarget[0].ReadPixels());
        Assert.Equal(originalNormals, scene.Normal.ReadPixels());
        Assert.Equal(originalPositions, scene.Position.ReadPixels());
    }

    /// <summary>SSAO restoration selects metadata by surviving depth, including zero-alpha writes and discard paths.</summary>
    [Theory]
    [InlineData(0, "surviving partial-alpha particle")]
    [InlineData(1, "surviving zero-alpha depth write")]
    [InlineData(2, "later foreground receiver")]
    [InlineData(3, "untouched receiver")]
    public void RestoreSelectsExpectedMetadataForUploadedSamples(int pixel, string scenario)
    {
        EnsureShaderTestAvailable();
        using var scene = new ParticleSsaoCaptureFixture();
        scene.CaptureUploadedSamples();
        scene.ReplaceForegroundReceiver();
        scene.ResolveReceivers(Programs.Create<SceneColorParticleShaderProgram>(), TestFramework);
        Assert.Equal(new[] { .75f, .75f, .25f, .75f }, scene.Targets.ResolveTarget[0].ReadPixels());

        scene.RestoreMetadata(Programs.Create<SceneColorParticleSsaoShaderProgram>(), TestFramework);

        float[] expectedNormal = pixel switch
        {
            0 => [.25f, .5f, .75f, .5f],
            1 => [.25f, .5f, .75f, 0],
            2 => [11, 12, 13, 14],
            _ => [1, 2, 3, 4]
        };
        float[] expectedPosition = pixel switch
        {
            0 or 1 => [10, 20, 30, .125f],
            2 => [15, 16, 17, 18],
            _ => [5, 6, 7, 8]
        };
        Assert.True(expectedNormal.SequenceEqual(scene.Normal.ReadPixelsRegion(pixel, 0, 1, 1)), scenario);
        Assert.True(expectedPosition.SequenceEqual(scene.Position.ReadPixelsRegion(pixel, 0, 1, 1)), scenario);
    }

    /// <summary>A new capture clears owned particle metadata without clearing the borrowed material receiver images.</summary>
    [Fact]
    public void BeginningAnotherCaptureClearsOnlyOwnedMetadata()
    {
        EnsureShaderTestAvailable();
        using var scene = new ParticleSsaoCaptureFixture();
        scene.CaptureUploadedSamples();
        float[] originalNormals = scene.Normal.ReadPixels();
        float[] originalPositions = scene.Position.ReadPixels();

        scene.Targets.BeginCapture();
        scene.Targets.EndCapture();

        Assert.All(scene.Targets.DrawTarget[2].ReadPixels(), value => Assert.Equal(0, value));
        Assert.All(scene.Targets.DrawTarget[3].ReadPixels(), value => Assert.Equal(0, value));
        Assert.Equal(originalNormals, scene.Normal.ReadPixels());
        Assert.Equal(originalPositions, scene.Position.ReadPixels());
    }

    /// <summary>Retiring capture storage leaves borrowed engine images live and readable after the disposal queue drains.</summary>
    [Fact]
    public void DisposingCapturePreservesBorrowedReceiverImages()
    {
        EnsureShaderTestAvailable();
        using var scene = new ParticleSsaoCaptureFixture();
        var isolatedNormal = scene.Targets.DrawTarget[2];
        var isolatedPosition = scene.Targets.DrawTarget[3];
        float[] originalNormals = scene.Normal.ReadPixels();
        float[] originalPositions = scene.Position.ReadPixels();

        scene.Targets.Dispose();
        GpuResourceManagerSystem.CaptureDisposalQueue().DrainPending();

        Assert.False(isolatedNormal.IsValid);
        Assert.False(isolatedPosition.IsValid);
        Assert.True(scene.Material.IsValid);
        Assert.True(scene.Glow.IsValid);
        Assert.True(scene.Depth.IsValid);
        Assert.Equal(originalNormals, scene.Normal.ReadPixels());
        Assert.Equal(originalPositions, scene.Position.ReadPixels());
        Assert.Equal(new[] { .75f, .75f, .75f, .75f }, scene.Depth.ReadPixels());
    }
    #endregion
}
