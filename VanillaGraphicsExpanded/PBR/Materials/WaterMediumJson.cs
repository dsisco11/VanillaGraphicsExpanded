using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Optional homogeneous water parameters; coefficients use inverse metres, all other fields are dimensionless.</summary>
internal sealed class WaterMediumJson
{
    [JsonProperty("additionalAbsorptionPerMetre")] public float[]? AdditionalAbsorptionPerMetre { get; set; }
    [JsonProperty("scatteringPerMetre")] public float[]? ScatteringPerMetre { get; set; }
    [JsonProperty("density")] public float? Density { get; set; }
    [JsonProperty("anisotropy")] public float? Anisotropy { get; set; }
}
