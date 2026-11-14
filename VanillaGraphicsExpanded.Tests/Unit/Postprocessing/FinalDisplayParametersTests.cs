using Moq;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Unit.Postprocessing;

/// <summary>Checks the native display settings and environmental effect adapter independently of GPU packing.</summary>
[Collection("CPU state")]
public sealed class FinalDisplayParametersTests
{
    #region Public API
    /// <summary>Native gamma is relative to its neutral setting while other controls retain their installed scaling and combine environmental contributions once.</summary>
    [Fact]
    public void CaptureNormalizesNativeGammaAndPreservesScreenEffects()
    {
        // Arrange
        var uniforms=new DefaultShaderUniforms
        {
            DropShadowIntensity=1.3f,ExtraContrastLevel=.2f,SepiaLevel=.15f,ExtraSepia=.3f,
            WindWaveCounter=17,GlitchStrength=.4f,DamageVignetting=.7f,DamageVignettingSide=-.2f,FrostVignetting=.9f
        };
        var api=new Mock<ICoreClientAPI>{DefaultValue=DefaultValue.Mock};
        api.SetupGet(value=>value.Render.ShaderUniforms).Returns(uniforms);

        // Act
        var captured=FinalDisplayParameters.Capture(api.Object);

        // Assert
        Assert.Equal(ClientSettings.GammaLevel / 3f,captured.Grading.Gamma);
        Assert.Equal(ClientSettings.ExtraGammaLevel,captured.Grading.ExtraGamma);
        Assert.Equal(ClientSettings.BrightnessLevel+(1.3f*2-1.66f)/3,captured.Grading.Brightness);
        Assert.Equal(.2f,captured.Grading.Contrast);
        Assert.Equal(.15f+.3f,captured.Sepia);Assert.Equal(17,captured.WindPhase);Assert.Equal(.4f,captured.GlitchStrength);
        Assert.Equal(.7f,captured.DamageVignette);Assert.Equal(-.2f,captured.SideDamageVignette);Assert.Equal(.9f,captured.FrostVignette);

        // Arrange a changed source so the second capture can be compared with the retained first snapshot.
        uniforms.DropShadowIntensity=0;uniforms.WindWaveCounter=18;

        // Act
        var next=FinalDisplayParameters.Capture(api.Object);

        // Assert
        Assert.Equal(ClientSettings.BrightnessLevel,next.Grading.Brightness);
        Assert.Equal(17,captured.WindPhase);Assert.Equal(18,next.WindPhase);
    }
    #endregion
}
