using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks the production configuration boundary and retired program removal.</summary>
public sealed class CloudVolumeSelectionTests
{
    #region Public API
    /// <summary>Applied features and resolution remain pending until early preparation.</summary>
    [Fact]
    public void ConfigurationPublishesOnlyAtPreparation()
    {
        var config=new CloudRenderingSettings();var staged=new CloudVolumeConfiguration();
        Assert.Equal(CloudVolumeViewSettings.Default,config.Capture());
        foreach(var (reference,scale,divisor) in new[]{(false,CloudTraceScale.Half,2),(true,CloudTraceScale.Quarter,1),(false,CloudTraceScale.Quarter,4)})
        {
            var previous=staged.Settings;config.TraceScale=scale;config.FullResolutionReference=reference;
            config.Detail=false;config.SelfShadowing=false;config.Diffuse=false;config.StopTransmittance=0;
            staged.Request(config);Assert.Equal(previous,staged.Settings);staged.Apply();
            Assert.Equal(new CloudVolumeViewSettings(false,false,false,0,divisor),staged.Settings);
        }
        config.TraceScale=(CloudTraceScale)3;config.StopTransmittance=float.NaN;
        Assert.Equal(4,config.Capture().TraceDivisor);Assert.Equal(.005f,config.StopTransmittance);
        config.LightingRaysPerUpdate=int.MaxValue;Assert.Equal(327680,config.Capture().LightingRaysPerUpdate);
        config.LightingRaysPerUpdate=-1;Assert.Equal(2560,config.Capture().LightingRaysPerUpdate);
        config.LightingRaysPerUpdate=9000;Assert.Equal(7680,config.Capture().LightingRaysPerUpdate);
        var copy=JsonConvert.DeserializeObject<CloudRenderingSettings>(JsonConvert.SerializeObject(config))!;
        Assert.Equal(config.Capture(),copy.Capture());
        Assert.Throws<ArgumentOutOfRangeException>(()=>(CloudVolumeViewSettings.Default with{TraceDivisor=3}).Validate());
    }
    /// <summary>Only supported cloud controls and retained shader inputs remain discoverable.</summary>
    [Fact]
    public void ProductionControlsAndCatalogExcludeRetiredPaths()
    {
        var document=JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"assets/config/configlib-patches.json")));
        var controls=document["settings"]!.Children<JProperty>().SelectMany(p=>p.Value.Children<JProperty>()).Select(p=>p.Value)
            .Where(p=>((string?)p["code"])?.StartsWith("Clouds.")==true).ToArray();
        string[] expected=["Detail","SelfShadowing","Diffuse","StopTransmittance","TraceScale","FullResolutionReference","LightingRaysPerUpdate"];
        Assert.Equal(expected.Select(s=>"Clouds."+s).Order(),controls.Select(p=>(string)p["code"]!).Order());
        var serialized=JObject.Parse(JsonConvert.SerializeObject(new CloudRenderingSettings()));
        Assert.Equal(expected.Order(),serialized.Properties().Select(p=>p.Name).Order());
        var identities=GeneratedShaderCatalog.Programs().SelectMany(p=>p.Stages).Select(s=>s.Identity).Distinct().ToArray();
        Assert.Contains("cloud_volume/view.fsh",identities);Assert.Contains("cloud_volume/temporal.fsh",identities);
        Assert.Contains("cloud_simple.fsh",identities);
        Assert.DoesNotContain(identities,s=>s.StartsWith("cloud_lighting_volume/")||s is "cloud_volume.fsh" or "cloud_volume_trace.fsh" or "cloud_volume_temporal.fsh" or "cloud_volume_resolve.fsh" or "cloud_hierarchy.csh" or "cloud_shadow_cache.csh" or "tests/cloud-traversal.csh");
    }
    #endregion
}
