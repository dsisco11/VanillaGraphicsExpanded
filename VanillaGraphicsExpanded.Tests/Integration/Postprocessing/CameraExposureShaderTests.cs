using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Integration.Postprocessing;

/// <summary>Checks native sampler compatibility and compute-dispatch state restoration at GPU boundaries.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class CameraExposureShaderTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>A production 2D sampler rejects borrowed metadata that identifies a 3D texture.</summary>
    [Fact]
    public void BorrowedTextureTargetMismatchIsRejected()
    {
        // Arrange
        EnsureShaderTestAvailable();
        var shader = Programs.CreateCompute(CameraHistogramShaderProgram.Create);
        Assert.True(shader.EnsureReady());
        using var external = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription
        {
            Dimension = TextureDimension.Three,
            Format = TextureFormat.Rgba16f,
            Width = 2,
            Height = 2,
            Depth = 2,
            MipLevels = 1,
            Storage = ResourceStorageMode.Mutable,
            Filter = TextureFilterMode.Nearest,
            BaseLevel = 0,
            MaxLevel = 0,
            Label = "Borrowed.Mismatch"
        }).RequireResource();
        using var borrowed = new BorrowedTexture(external.TextureId);
        var binding = shader.ProgramLayout.BinaryInterface!.PreparedBindings.Resolve(
            GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "sceneRadiance"));

        // Assert
        Assert.True(binding.Active);
        // Act
        var error = Assert.Throws<InvalidOperationException>(() => ShaderResourcePreparation.ValidateSampler(binding, borrowed));

        // Assert
        Assert.Contains("Incompatible texture target", error.Message);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Dispatch preserves caller storage bindings and the selected executable without publishing write history.</summary>
    [Fact]
    public void ComputeBoundaryRestoresStorageAndKnownProgram()
    {
        // Arrange
        EnsureShaderTestAvailable();
        var meter = Programs.CreateCompute(CameraHistogramShaderProgram.Create);
        var adapt = Programs.CreateCompute(CameraAdaptShaderProgram.Create);
        using var targets = new CameraExposureTargets();
        using var source = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba32f, [.18f, .18f, .18f, 1f]);
        using var camera = TestFrameCamera.CreateIdentity(1, 1);
        using var external = ExposureTestStorage.Create(7);
        meter.SceneRadiance = source;
        meter.Histogram = targets.Histogram;
        meter.Capture(Settings(), true);
        adapt.Histogram = targets.Histogram;
        adapt.PreviousExposure = targets.Exposure;
        adapt.OutputExposure = targets.WriteTarget;
        adapt.FrameInputs = camera;
        adapt.Capture(Settings(), true);
        int published = targets.Exposure.BufferId;
        for (int slot = 0; slot < 3; slot++) external.BindBase(slot);

        try
        {
            // Act
            Assert.True(CameraExposureDispatch.TryRun(meter, adapt));

            // Assert
            for (int slot = 0; slot < 3; slot++)
            {
                GL.GetInteger((GetIndexedPName)All.ShaderStorageBufferBinding, slot, out int selected);
                Assert.Equal(external.BufferId, selected);
            }
            AssertClose(0, ExposureTestStorage.Read<float>(targets.WriteTarget)[0]);

            // Arrange
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, meter.ProgramId);

            // Act
            Assert.True(CameraExposureDispatch.TryRun(meter, adapt));

            // Assert
            Assert.Equal(published, targets.Exposure.BufferId);
            Assert.Equal(meter.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
        }
        finally
        {
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
            for (int slot = 0; slot < 3; slot++) GpuShaderStorageBuffer.UnbindBase(slot);
        }

        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    #endregion

    #region Private
/// <summary>Uses bounded automatic camera settings for a deterministic one-sample dispatch.</summary>
    private static CameraExposureParameters Settings() => new(true, 0, 0, -12, 12, .18f, 0, 1, 1, 3, false);

    /// <summary>Checks an exposure value with the bounded numerical tolerance used by this test.</summary>
    private static void AssertClose(float expected, float actual, float tolerance = .0003f)
        => Assert.InRange(actual, expected - tolerance, expected + tolerance);
    #endregion
}
