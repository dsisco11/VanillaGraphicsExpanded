using System.Collections.Generic;

using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal sealed class PbrMaterialDefinitionsJsonFile
{
    [JsonProperty("$schema")]
    public string? Schema { get; set; }

    [JsonProperty("version")]
    public int Version { get; set; }

    [JsonProperty("notes")]
    public List<string>? Notes { get; set; }

    [JsonProperty("defaults")]
    public PbrMaterialDefaultsJson? Defaults { get; set; }

    [JsonProperty("materials")]
    public Dictionary<string, PbrMaterialDefinitionJson>? Materials { get; set; }

    [JsonProperty("mapping")]
    public List<PbrMaterialMappingRuleJson>? Mapping { get; set; }
}

internal sealed class PbrMaterialDefaultsJson
{
    [JsonProperty("roughness")]
    public float? Roughness { get; set; }

    [JsonProperty("metallic")]
    public float? Metallic { get; set; }

    [JsonProperty("emissive")]
    public float? Emissive { get; set; }

    [JsonProperty("noise")]
    public PbrMaterialNoiseJson? Noise { get; set; }

    [JsonProperty("scale")]
    public PbrOverrideScaleJson? Scale { get; set; }
}

internal sealed class PbrMaterialDefinitionJson
{
    [JsonProperty("roughness")]
    public float? Roughness { get; set; }

    [JsonProperty("metallic")]
    public float? Metallic { get; set; }

    [JsonProperty("emissive")]
    public float? Emissive { get; set; }

    [JsonProperty("noise")]
    public PbrMaterialNoiseJson? Noise { get; set; }

    [JsonProperty("scale")]
    public PbrOverrideScaleJson? Scale { get; set; }

    [JsonProperty("notes")]
    public string? Notes { get; set; }

    [JsonProperty("priority")]
    public int? Priority { get; set; }
}

internal sealed class PbrMaterialNoiseJson
{
    [JsonProperty("roughness")]
    public float? Roughness { get; set; }

    [JsonProperty("metallic")]
    public float? Metallic { get; set; }

    [JsonProperty("emissive")]
    public float? Emissive { get; set; }

    [JsonProperty("reflectivity")]
    public float? Reflectivity { get; set; }

    [JsonProperty("normals")]
    public float? Normals { get; set; }
}

internal sealed class PbrMaterialMappingRuleJson
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("description")]
    public string? Description { get; set; }

    [JsonProperty("priority")]
    public int? Priority { get; set; }

    [JsonProperty("match")]
    public PbrMaterialMatchJson? Match { get; set; }

    [JsonProperty("values")]
    public PbrMaterialMappingValuesJson? Values { get; set; }
}

internal sealed class PbrMaterialMatchJson
{
    [JsonProperty("glob")]
    public string? Glob { get; set; }
}

internal sealed class PbrMaterialMappingValuesJson
{
    [JsonProperty("material")]
    public string? Material { get; set; }

    [JsonProperty("overrides")]
    public PbrMaterialMappingOverridesJson? Overrides { get; set; }
}

internal sealed class PbrMaterialMappingOverridesJson
{
    [JsonProperty("materialParams")]
    public string? MaterialParams { get; set; }

    [JsonProperty("normalHeight")]
    public string? NormalHeight { get; set; }

    [JsonProperty("scale")]
    public PbrOverrideScaleJson? Scale { get; set; }
}
