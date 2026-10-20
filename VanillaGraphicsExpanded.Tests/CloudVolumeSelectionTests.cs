using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks early-boundary selection and independent candidate settings without a native game lifetime.</summary>
public sealed class CloudVolumeSelectionTests
{
    #region Public API
    /// <summary>Pending configuration cannot switch a frozen frame; retirement precedes new preparation and preserves mode admission.</summary>
    [Fact]
    public void SelectionRetiresBeforeReplacementOnlyAtExplicitBoundary()
    {
        var selection=new CloudVolumeSelection();var config=new CloudRenderingSettings();
        using var ownership=new CloudModeOwnership();ownership.Update(true,1);
        var existing=new Consumer();Assert.True(ownership.TryActivate(CloudMode.Volumetric,existing,true));
        config.VolumeRenderer=CloudVolumeRendererKind.LightingVolumeCandidate;selection.Request(config);
        Assert.Equal(CloudVolumeRendererKind.Existing,selection.Renderer);Assert.Equal(0,existing.Disposals);
        selection.Apply(()=>
        {
            Assert.Equal(CloudVolumeRendererKind.Existing,selection.Renderer);ownership.Dispose();
        });
        Assert.Equal(1,existing.Disposals);Assert.False(ownership.Owns(CloudMode.Volumetric));
        ownership.Update(true,1);var candidate=new Consumer();
        Assert.False(ownership.TryActivate(CloudMode.Volumetric,candidate,false));
        Assert.False(ownership.Owns(CloudMode.Volumetric));
        Assert.True(ownership.TryActivate(CloudMode.Volumetric,candidate,true));
        // Ordinary per-frame reconciliation, including missing draw inputs, retains the one admitted owner.
        ownership.Update(true,1);Assert.True(ownership.Owns(CloudMode.Volumetric));
        config.LightingVolume.ViewMaximum=12;selection.Request(config);selection.Apply(()=>throw new Exception("View-only change retired ownership."));
        Assert.Equal(12,selection.Settings.Maximum);Assert.Equal(0,candidate.Disposals);
        config.VolumeRenderer=CloudVolumeRendererKind.StoredVolumeCandidate;
        config.StoredVolume.Detail=false;selection.Request(config);
        Assert.Equal(CloudVolumeRendererKind.LightingVolumeCandidate,selection.Renderer);
        selection.Apply(ownership.Dispose);Assert.Equal(1,candidate.Disposals);
        ownership.Update(true,1);var stored=new Consumer();Assert.True(ownership.TryActivate(CloudMode.Volumetric,stored,true));
        Assert.False(selection.StoredSettings.Detail);
        config.StoredVolume.StopTransmittance=0;selection.Request(config);selection.Apply(()=>throw new Exception("Stored view-only change retired ownership."));
        Assert.Equal(0,selection.StoredSettings.Stop);
        config.VolumeRenderer=CloudVolumeRendererKind.Existing;selection.Request(config);selection.Apply(ownership.Dispose);
        Assert.Equal(1,stored.Disposals);
        Assert.Equal(1,candidate.Disposals);Assert.Equal(CloudVolumeRendererKind.Existing,selection.Renderer);
    }
    /// <summary>Candidate fields sanitize and serialize separately while current defaults and ConfigLib controls stay consistent.</summary>
    [Fact]
    public void CandidateConfigurationKeepsExistingQualityAndDefaultsIndependent()
    {
        var config=JsonConvert.DeserializeObject<CloudRenderingSettings>("{}")!;
        var existing=config.Capture();Assert.Equal(CloudVolumeRendererKind.Existing,config.VolumeRenderer);
        Assert.Equal(LightingVolumeViewSettings.Default,config.LightingVolume.Capture());
        config.LightingVolume.ViewMaximum=999;config.LightingVolume.ViewBudgetDistance=float.NaN;
        config.LightingVolume.StopTransmittance=-1;config.LightingVolume.Diffuse=false;config.VolumeRenderer=(CloudVolumeRendererKind)99;
        config.Sanitize();Assert.Equal(existing,config.Capture());Assert.Equal(CloudVolumeRendererKind.Existing,config.VolumeRenderer);
        Assert.Equal(new LightingVolumeViewSettings(96,10000,0,true,true,false),config.LightingVolume.Capture());
        var roundtrip=JsonConvert.DeserializeObject<CloudRenderingSettings>(JsonConvert.SerializeObject(config))!;
        Assert.Equal(config.LightingVolume.Capture(),roundtrip.LightingVolume.Capture());
        Assert.Equal(VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudVolumeViewSettings.Default,config.StoredVolume.Capture());
        config.StoredVolume.StopTransmittance=float.NaN;config.StoredVolume.Sanitize();Assert.Equal(.005f,config.StoredVolume.StopTransmittance);
        var document=JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"assets/config/configlib-patches.json")));
        var controls=document["settings"]!.Children<JProperty>().SelectMany(p=>p.Value.Children<JProperty>()).Select(p=>p.Value).ToArray();
        Assert.Equal(0,controls.Single(p=>(string?)p["code"]=="Clouds.VolumeRenderer")["default"]!.Value<int>());
        Assert.Contains(2,controls.Single(p=>(string?)p["code"]=="Clouds.VolumeRenderer")["values"]!.Values<int>());
        foreach(string field in new[]{"Detail","SelfShadowing","Diffuse","StopTransmittance"})
            Assert.Single(controls,p=>(string?)p["code"]=="Clouds.StoredVolume."+field);
        foreach(string field in new[]{"ViewMaximum","ViewBudgetDistance","StopTransmittance","Detail","SelfShadowing","Diffuse"})
            Assert.Single(controls,p=>(string?)p["code"]=="Clouds.LightingVolume."+field);
    }
    #endregion
    #region Private
    /// <summary>Counts only selected-owner retirement; failed admission leaves ownership with the caller.</summary>
    private sealed class Consumer:IDisposable
    {
        internal int Disposals {get;private set;}
        #region Public API
        /// <summary>Records each release so duplicate disposal is observable.</summary>
        public void Dispose()=>Disposals++;
        #endregion
    }
    #endregion
}
