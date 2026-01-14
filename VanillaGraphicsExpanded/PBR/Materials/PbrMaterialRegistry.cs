using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal sealed class PbrMaterialRegistry
{
    public static PbrMaterialRegistry Instance { get; } = new();

    private readonly List<PbrMaterialDefinitionsSource> sources = new();

    private readonly Dictionary<string, PbrMaterialDefinition> materialById = new(StringComparer.Ordinal);
    private readonly Dictionary<AssetLocation, string> materialIdByTexture = new();
    private readonly Dictionary<string, int> materialIndexById = new(StringComparer.Ordinal);
    private PbrMaterialDefinition[] materialsByIndex = Array.Empty<PbrMaterialDefinition>();

    private readonly List<PbrMaterialMappingRule> mappingRules = new();

    private PbrMaterialRegistry()
    {
    }

    public IReadOnlyList<PbrMaterialDefinitionsSource> Sources => sources;

    public IReadOnlyDictionary<string, PbrMaterialDefinition> MaterialById => materialById;

    public IReadOnlyDictionary<AssetLocation, string> MaterialIdByTexture => materialIdByTexture;

    public IReadOnlyDictionary<string, int> MaterialIndexById => materialIndexById;

    public IReadOnlyList<PbrMaterialDefinition> MaterialsByIndex => materialsByIndex;

    public IReadOnlyList<PbrMaterialMappingRule> MappingRules => mappingRules;

    public bool IsInitialized { get; private set; }

    public void Initialize(ICoreAPI api, bool strict = false)
    {
        if (api == null) throw new ArgumentNullException(nameof(api));

        // Intentionally load at AssetsLoaded or later.
        List<IAsset> assets = api.Assets.GetManyInCategory(AssetCategory.config.Code, "vge/material_definitions.json", domain: null, loadAsset: true);
        List<AssetLocation> textureLocations = api.Assets.GetLocations("textures/", domain: null);

        IReadOnlyList<PbrMaterialDefinitionsSource> parsedSources = ParseSources(api.Logger, assets, strict);

        InitializeFromParsedSources(
            logger: api.Logger,
            parsedSources: parsedSources,
            textureLocations: textureLocations,
            strict: strict);
    }

    /// <summary>
    /// Test seam: initializes the registry from already-parsed sources and a fixed list of texture locations.
    /// This avoids the need for a full <see cref="ICoreAPI"/> instance in unit tests while preserving
    /// deterministic behavior.
    /// </summary>
    internal void InitializeFromParsedSources(
        ILogger logger,
        IReadOnlyList<PbrMaterialDefinitionsSource> parsedSources,
        IReadOnlyList<AssetLocation> textureLocations,
        bool strict)
    {
        if (logger == null) throw new ArgumentNullException(nameof(logger));
        if (parsedSources == null) throw new ArgumentNullException(nameof(parsedSources));
        if (textureLocations == null) throw new ArgumentNullException(nameof(textureLocations));

        sources.Clear();
        sources.AddRange(parsedSources);

        materialById.Clear();
        materialIdByTexture.Clear();
        materialIndexById.Clear();
        materialsByIndex = Array.Empty<PbrMaterialDefinition>();
        mappingRules.Clear();

        BuildMergedMaterials();
        BuildMappingRules(logger, strict);
        BuildTextureMappings(logger, textureLocations);
        AssignMaterialIndices();

        int totalMaterials = sources.Sum(s => s.File.Materials?.Count ?? 0);
        int totalMappingRules = sources.Sum(s => s.File.Mapping?.Count ?? 0);

        logger.Notification(
            "[VGE] Loaded {0} pbr_material_definitions.json file(s): {1} material(s), {2} mapping rule(s)",
            sources.Count,
            totalMaterials,
            totalMappingRules);

        logger.Notification(
            "[VGE] Material registry built: {0} merged material(s), {1} mapped texture(s), {2} rule(s)",
            materialById.Count,
            materialIdByTexture.Count,
            mappingRules.Count);

        IsInitialized = true;
    }

    public bool TryGetMaterial(string materialId, out PbrMaterialDefinition definition)
    {
        return materialById.TryGetValue(NormalizeMaterialId(materialId), out definition);
    }

    public bool TryGetMaterialId(AssetLocation texture, out string materialId)
    {
        return materialIdByTexture.TryGetValue(texture, out materialId!);
    }

    public bool TryGetMaterial(AssetLocation texture, out PbrMaterialDefinition definition)
    {
        if (!materialIdByTexture.TryGetValue(texture, out string? materialId))
        {
            definition = default;
            return false;
        }

        return materialById.TryGetValue(materialId, out definition);
    }

    public void Clear()
    {
        sources.Clear();
        materialById.Clear();
        materialIdByTexture.Clear();
        materialIndexById.Clear();
        materialsByIndex = Array.Empty<PbrMaterialDefinition>();
        mappingRules.Clear();
        IsInitialized = false;
    }

    private static IReadOnlyList<PbrMaterialDefinitionsSource> ParseSources(ILogger logger, List<IAsset> assets, bool strict)
    {
        if (logger == null) throw new ArgumentNullException(nameof(logger));
        if (assets == null) throw new ArgumentNullException(nameof(assets));

        var ordered = assets
            .OrderBy(a => a.Location.Domain, StringComparer.Ordinal)
            .ThenBy(a => a.Location.Path, StringComparer.Ordinal)
            .ToList();

        var parsed = new List<PbrMaterialDefinitionsSource>(ordered.Count);

        foreach (IAsset asset in ordered)
        {
            try
            {
                PbrMaterialDefinitionsJsonFile file = asset.ToObject<PbrMaterialDefinitionsJsonFile>();

                if (file.Version != 1)
                {
                    logger.Warning(
                        "[VGE] Skipping {0}: unsupported pbr_material_definitions.json version={1}",
                        asset.Location,
                        file.Version);
                    continue;
                }

                parsed.Add(new PbrMaterialDefinitionsSource(asset.Location.Domain, asset.Location, file));
            }
            catch (Exception ex)
            {
                logger.Error(
                    "[VGE] Failed to parse {0} as pbr_material_definitions.json: {1}",
                    asset.Location,
                    ex);

                if (strict)
                {
                    throw;
                }
            }
        }

        return parsed;
    }

    private void BuildMergedMaterials()
    {
        // Deterministic merge order: stable domain/modid, then file (asset path) order.
        // For material id collisions: higher priority wins; on tie, later in deterministic load order wins.

        foreach (PbrMaterialDefinitionsSource source in sources)
        {
            PbrMaterialDefaults defaults = BuildDefaults(source.File);

            if (source.File.Materials == null) continue;

            foreach ((string key, PbrMaterialDefinitionJson json) in source.File.Materials)
            {
                string materialId = ResolveMaterialId(key, source.Domain);

                PbrMaterialDefinition definition = BuildDefinition(defaults, json);

                if (!materialById.TryGetValue(materialId, out PbrMaterialDefinition existing))
                {
                    materialById[materialId] = definition;
                    continue;
                }

                int existingPriority = existing.Priority;
                int newPriority = definition.Priority;

                if (newPriority > existingPriority)
                {
                    materialById[materialId] = definition;
                }
                else if (newPriority == existingPriority)
                {
                    // Tie-break: later in deterministic load order wins
                    materialById[materialId] = definition;
                }
                else
                {
                    // keep existing
                }
            }

        }
    }

    private void BuildMappingRules(ILogger logger, bool strict)
    {
        // Deterministic merge order for rules: stable domain/modid, then file (asset path), then mapping list order.
        // Collision for a given texture: higher rule priority wins; on tie, later rule wins.

        int orderIndex = 0;
        foreach (PbrMaterialDefinitionsSource source in sources)
        {
            if (source.File.Mapping == null)
            {
                continue;
            }

            for (int i = 0; i < source.File.Mapping.Count; i++)
            {
                PbrMaterialMappingRuleJson rule = source.File.Mapping[i];
                string? glob = rule.Match?.Glob;

                if (string.IsNullOrWhiteSpace(glob))
                {
                    logger.Warning("[VGE] Skipping mapping rule in {0}: missing match.glob", source.Location);
                    continue;
                }

                string? materialRef = rule.Values?.Material;
                if (string.IsNullOrWhiteSpace(materialRef))
                {
                    logger.Warning("[VGE] Skipping mapping rule in {0}: missing values.material", source.Location);
                    continue;
                }

                string materialId = ResolveMaterialId(materialRef, source.Domain);

                Regex regex;
                try
                {
                    regex = PbrGlobstar.CompileRegex(glob);
                }
                catch (Exception ex)
                {
                    logger.Error("[VGE] Invalid glob pattern in {0}: '{1}' ({2})", source.Location, glob, ex.Message);
                    if (strict) throw;
                    continue;
                }

                int priority = rule.Priority ?? 0;

                mappingRules.Add(new PbrMaterialMappingRule(
                    OrderIndex: orderIndex,
                    Priority: priority,
                    Source: source.Location,
                    Id: rule.Id,
                    Glob: glob,
                    MatchRegex: regex,
                    MaterialId: materialId));

                orderIndex++;
            }
        }
    }

    private void BuildTextureMappings(ILogger logger, IReadOnlyList<AssetLocation> textureLocations)
    {
        // Eagerly pre-expand all globs into concrete texture locations.
        // Candidate set: all assets under textures/ across all domains.
        List<AssetLocation> ordered = textureLocations
            .Select(NormalizeTextureLocation)
            .OrderBy(loc => loc.Domain, StringComparer.Ordinal)
            .ThenBy(loc => loc.Path, StringComparer.Ordinal)
            .ToList();

        int mapped = 0;
        int unmapped = 0;
        int unknownMaterialRefs = 0;

        foreach (AssetLocation texture in ordered)
        {
            string key = ToCanonicalAssetKey(texture);
            PbrMaterialMappingRule? winner = null;

            foreach (PbrMaterialMappingRule rule in mappingRules)
            {
                if (!rule.MatchRegex.IsMatch(key))
                {
                    continue;
                }

                if (winner == null)
                {
                    winner = rule;
                    continue;
                }

                if (rule.Priority > winner.Value.Priority)
                {
                    winner = rule;
                    continue;
                }
                // Tie-break: keep the first matching rule when priorities tie.
                // This allows authors to place broad catch-all rules at the end without overriding
                // more specific earlier rules.
            }

            if (winner == null)
            {
                unmapped++;
                continue;
            }

            string materialId = winner.Value.MaterialId;
            if (!materialById.ContainsKey(materialId))
            {
                // Policy: warn on unknown MaterialId referenced by a mapping rule
                logger.Warning(
                    "[VGE] Mapping rule references unknown material '{0}' (rule '{1}', source {2})",
                    materialId,
                    winner.Value.Id ?? "(no id)",
                    winner.Value.Source);
                unknownMaterialRefs++;
                continue;
            }

            materialIdByTexture[texture] = materialId;
            mapped++;
        }

        logger.Notification(
            "[VGE] Texture mapping scan: {0} texture(s) scanned, {1} mapped, {2} unmapped, {3} unknown material reference(s)",
            ordered.Count,
            mapped,
            unmapped,
            unknownMaterialRefs);
    }

    private static AssetLocation NormalizeTextureLocation(AssetLocation location)
    {
        // Depending on engine call site, AssetManager.GetLocations("textures/") may return either:
        // - "textures/..." (full path), or
        // - "..." relative to the requested base path.
        // We normalize so mapping rules can reliably target "assets/<domain>/textures/...".

        string domain = location.Domain.ToLowerInvariant();

        string path = location.Path.Replace('\\', '/').ToLowerInvariant();
        if (!path.StartsWith("textures/", StringComparison.Ordinal))
        {
            path = "textures/" + path.TrimStart('/');
        }

        return new AssetLocation(domain, path);
    }

    private void AssignMaterialIndices()
    {
        // Deterministic indexing: sort by MaterialId ordinal.
        string[] ids = materialById.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();

        materialsByIndex = new PbrMaterialDefinition[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            string id = ids[i];
            materialIndexById[id] = i;
            materialsByIndex[i] = materialById[id];
        }
    }

    private static PbrMaterialDefaults BuildDefaults(PbrMaterialDefinitionsJsonFile file)
    {
        float roughness = file.Defaults?.Roughness ?? 0.85f;
        float metallic = file.Defaults?.Metallic ?? 0.0f;
        float emissive = file.Defaults?.Emissive ?? 0.0f;

        var noise = file.Defaults?.Noise;
        return new PbrMaterialDefaults(
            Roughness: roughness,
            Metallic: metallic,
            Emissive: emissive,
            Noise: new PbrMaterialNoise(
                Roughness: noise?.Roughness ?? 0.0f,
                Metallic: noise?.Metallic ?? 0.0f,
                Emissive: noise?.Emissive ?? 0.0f,
                Reflectivity: noise?.Reflectivity ?? 0.0f,
                Normals: noise?.Normals ?? 0.0f));
    }

    private static PbrMaterialDefinition BuildDefinition(PbrMaterialDefaults defaults, PbrMaterialDefinitionJson json)
    {
        float roughness = json.Roughness ?? defaults.Roughness;
        float metallic = json.Metallic ?? defaults.Metallic;
        float emissive = json.Emissive ?? defaults.Emissive;

        PbrMaterialNoiseJson? noiseJson = json.Noise;
        PbrMaterialNoise noise = new(
            Roughness: noiseJson?.Roughness ?? defaults.Noise.Roughness,
            Metallic: noiseJson?.Metallic ?? defaults.Noise.Metallic,
            Emissive: noiseJson?.Emissive ?? defaults.Noise.Emissive,
            Reflectivity: noiseJson?.Reflectivity ?? defaults.Noise.Reflectivity,
            Normals: noiseJson?.Normals ?? defaults.Noise.Normals);

        return new PbrMaterialDefinition(
            Roughness: roughness,
            Metallic: metallic,
            Emissive: emissive,
            Noise: noise,
            Priority: json.Priority ?? 0,
            Notes: json.Notes);
    }

    private static string ResolveMaterialId(string materialKeyOrId, string sourceDomain)
    {
        string normalized = NormalizeMaterialId(materialKeyOrId);

        return normalized.Contains(':')
            ? normalized
            : $"{sourceDomain}:{normalized}";
    }

    private static string NormalizeMaterialId(string materialKeyOrId)
    {
        return materialKeyOrId.Trim().ToLowerInvariant();
    }

    private static string ToCanonicalAssetKey(AssetLocation location)
    {
        // AssetManager normalizes asset paths to lowercase; domain is also lowercase.
        return $"assets/{location.Domain}/{location.Path}";
    }
}

internal readonly record struct PbrMaterialDefinitionsSource(string Domain, AssetLocation Location, PbrMaterialDefinitionsJsonFile File);

internal readonly record struct PbrMaterialDefaults(float Roughness, float Metallic, float Emissive, PbrMaterialNoise Noise);

internal readonly record struct PbrMaterialMappingRule(
    int OrderIndex,
    int Priority,
    AssetLocation Source,
    string? Id,
    string Glob,
    Regex MatchRegex,
    string MaterialId);
