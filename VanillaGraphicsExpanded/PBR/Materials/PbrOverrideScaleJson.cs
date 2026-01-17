using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal sealed class PbrOverrideScaleJson
{
    [JsonProperty("roughness")]
    public float? Roughness { get; set; }

    [JsonProperty("metallic")]
    public float? Metallic { get; set; }

    [JsonProperty("emissive")]
    public float? Emissive { get; set; }

    [JsonProperty("normal")]
    public float? Normal { get; set; }

    [JsonProperty("depth")]
    public float? Depth { get; set; }
}
