using Moq;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks the native display settings and environmental effect adapter independently of GPU packing.</summary>
[Collection("GPU")]
public sealed class FinalDisplayParametersTests
{
    #region Public API
    /// <summary>Native gamma is relative to its neutral setting while other controls retain their installed scaling and combine environmental contributions once.</summary>
    [Fact]
    public void CaptureNormalizesNativeGammaAndPreservesScreenEffects()
    {
        var uniforms=new DefaultShaderUniforms
        {
            DropShadowIntensity=1.3f,ExtraContrastLevel=.2f,SepiaLevel=.15f,ExtraSepia=.3f,
            WindWaveCounter=17,GlitchStrength=.4f,DamageVignetting=.7f,DamageVignettingSide=-.2f,FrostVignetting=.9f
        };
        var api=new Mock<ICoreClientAPI>{DefaultValue=DefaultValue.Mock};
        api.SetupGet(value=>value.Render.ShaderUniforms).Returns(uniforms);
        var captured=FinalDisplayParameters.Capture(api.Object);
        Assert.Equal(ClientSettings.GammaLevel / 3f,captured.Grading.X);
        Assert.Equal(ClientSettings.ExtraGammaLevel,captured.Grading.Y);
        Assert.Equal(ClientSettings.BrightnessLevel+(1.3f*2-1.66f)/3,captured.Grading.Z);
        Assert.Equal(.2f,captured.Grading.W);
        Assert.Equal(.15f+.3f,captured.Effects.X);Assert.Equal(17,captured.Effects.Y);Assert.Equal(.4f,captured.Effects.Z);
        Assert.Equal(.7f,captured.Vignette.X);Assert.Equal(-.2f,captured.Vignette.Y);Assert.Equal(.9f,captured.Vignette.Z);
        uniforms.DropShadowIntensity=0;uniforms.WindWaveCounter=18;
        var next=FinalDisplayParameters.Capture(api.Object);
        Assert.Equal(ClientSettings.BrightnessLevel,next.Grading.Z);
        Assert.Equal(17,captured.Effects.Y);Assert.Equal(18,next.Effects.Y);
    }
    #endregion
}
