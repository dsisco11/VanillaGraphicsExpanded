using Newtonsoft.Json;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks persisted cache quality and its bounded resolution mapping.</summary>
public sealed class CloudShadowQualitySettingsTests
{
    #region Public API
    /// <summary>All levels round-trip, missing values retain the default and invalid settings cannot reach submission.</summary>
    [Fact]
    public void QualityPersistsAndMapsToSupportedResolutions()
    {
        Assert.Equal(1,JsonConvert.DeserializeObject<CloudRenderingSettings>("{}")!.Capture().ShadowQuality);
        Assert.Equal(1,CloudVolumeSettings.Default.ShadowQuality);
        int[] widths=[0,256,512,1024];
        for(int quality=0;quality<widths.Length;quality++)
        {
            string json=JsonConvert.SerializeObject(new CloudRenderingSettings {ShadowQuality=quality});
            var captured=JsonConvert.DeserializeObject<CloudRenderingSettings>(json)!.Capture();
            captured.Validate();
            Assert.Equal(quality,captured.ShadowQuality);
            Assert.Equal(widths[quality],captured.ShadowMapResolution);
            using var parameters=new CloudVolumeParameters();
            parameters.CaptureMap(20,default,1,captured);
            Assert.Equal(0f,BitConverter.ToSingle(parameters.Bytes.Slice(28,4)));
            foreach(var contract in new[]{CloudTraceProgram.Contract,CloudVolumeProgram.Contract})
            {
                var selected=new ShaderSettings(contract).With(CloudShaderOptions.SelfShadowing,quality!=0);
                Assert.Equal(quality!=0,ShaderOptionAccess.Get(selected,CloudShaderOptions.SelfShadowing));
            }
        }
        foreach(int invalid in new[]{-1,4})
        {
            Assert.Equal(Math.Clamp(invalid,0,3),new CloudRenderingSettings {ShadowQuality=invalid}.Capture().ShadowQuality);
            Assert.Throws<ArgumentOutOfRangeException>(()=>(CloudVolumeSettings.Default with {ShadowQuality=invalid}).Validate());
        }
    }
    #endregion
}
