using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Author properties whose omitted members inherit from the containing material/defaults.</summary>
internal class BRDFPropertiesJson
{
    [JsonProperty("roughness")] public float? Roughness { get; set; }
    [JsonProperty("metallic")] public float? Metallic { get; set; }
    [JsonProperty("emissive")] public float? Emissive { get; set; }
    [JsonProperty("noise")] public PbrMaterialNoiseJson? Noise { get; set; }
    [JsonProperty("scale")] public PbrOverrideScaleJson? Scale { get; set; }
}
