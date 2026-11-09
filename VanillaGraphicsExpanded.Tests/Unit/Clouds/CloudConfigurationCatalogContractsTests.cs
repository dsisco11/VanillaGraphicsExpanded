using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Clouds;

/// <summary>Checks published cloud controls and active shader catalog identities.</summary>
public sealed class CloudConfigurationCatalogContractsTests
{
    #region Public API
    /// <summary>Only supported cloud controls and retained shader inputs remain discoverable.</summary>
    [Fact]
    public void ProductionControlsAndCatalogExcludeRetiredPaths()
    {
        // Arrange: enumerate the supported cloud setting names expected in the built document.
        string[] expected=["Detail","SelfShadowing","Diffuse","Quality","TraceResolution","FullResolutionReference","LightingRaysPerUpdate"];
        // Act: read the built settings artifact, serialize defaults, and enumerate active shader-stage identities.
        var document=JObject.Parse(TestAssetSnapshot.ReadBuiltText(Path.Combine(AppContext.BaseDirectory,"assets/config/configlib-patches.json")));
        var controls=document["settings"]!.Children<JProperty>().SelectMany(p=>p.Value.Children<JProperty>()).Select(p=>p.Value)
            .Where(p=>((string?)p["code"])?.StartsWith("Clouds.")==true).ToArray();
        var serialized=JObject.Parse(JsonConvert.SerializeObject(new CloudRenderingSettings()));
        var identities=GeneratedShaderCatalog.Programs().SelectMany(p=>p.Stages).Select(s=>s.Identity).Distinct().ToArray();
        // Assert: only supported controls and retained production shader inputs are discoverable.
        Assert.Equal(expected.Select(s=>"Clouds."+s).Order(),controls.Select(p=>(string)p["code"]!).Order());
        Assert.Equal(expected.Order(),serialized.Properties().Select(p=>p.Name).Order());
        Assert.Contains("cloud_volume/view.fsh",identities);Assert.Contains("cloud_volume/temporal.fsh",identities);
        Assert.Contains("cloud_simple.fsh",identities);
        Assert.DoesNotContain(identities,s=>s.StartsWith("cloud_lighting_volume/")||s is "cloud_volume.fsh" or "cloud_volume_trace.fsh" or "cloud_volume_temporal.fsh" or "cloud_volume_resolve.fsh" or "cloud_hierarchy.csh" or "cloud_shadow_cache.csh" or "tests/cloud-traversal.csh");
    }
    #endregion
}
