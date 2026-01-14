using System;
using System.Collections.Generic;
using System.Linq;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal sealed class PbrMaterialRegistry
{
    public static PbrMaterialRegistry Instance { get; } = new();

    private readonly List<PbrMaterialDefinitionsSource> sources = new();

    private PbrMaterialRegistry()
    {
    }

    public IReadOnlyList<PbrMaterialDefinitionsSource> Sources => sources;

    public bool IsInitialized { get; private set; }

    public void Initialize(ICoreAPI api, bool strict = false)
    {
        if (api == null) throw new ArgumentNullException(nameof(api));

        sources.Clear();

        // Intentionally load at AssetsLoaded or later.
        List<IAsset> assets = api.Assets.GetMany("materials/pbr_material_definitions.json", domain: null, loadAsset: true);

        var ordered = assets
            .OrderBy(a => a.Location.Domain, StringComparer.Ordinal)
            .ThenBy(a => a.Location.Path, StringComparer.Ordinal)
            .ToList();

        foreach (IAsset asset in ordered)
        {
            try
            {
                PbrMaterialDefinitionsJsonFile file = asset.ToObject<PbrMaterialDefinitionsJsonFile>();

                if (file.Version != 1)
                {
                    api.Logger.Warning(
                        "[VGE] Skipping {0}: unsupported pbr_material_definitions.json version={1}",
                        asset.Location,
                        file.Version);
                    continue;
                }

                sources.Add(new PbrMaterialDefinitionsSource(asset.Location.Domain, asset.Location, file));
            }
            catch (Exception ex)
            {
                api.Logger.Error(
                    "[VGE] Failed to parse {0} as pbr_material_definitions.json: {1}",
                    asset.Location,
                    ex);

                if (strict)
                {
                    throw;
                }
            }
        }

        int totalMaterials = sources.Sum(s => s.File.Materials?.Count ?? 0);
        int totalMappingRules = sources.Sum(s => s.File.Mapping?.Count ?? 0);

        api.Logger.Notification(
            "[VGE] Loaded {0} pbr_material_definitions.json file(s): {1} material(s), {2} mapping rule(s)",
            sources.Count,
            totalMaterials,
            totalMappingRules);

        IsInitialized = true;
    }

    public void Clear()
    {
        sources.Clear();
        IsInitialized = false;
    }
}

internal readonly record struct PbrMaterialDefinitionsSource(string Domain, AssetLocation Location, PbrMaterialDefinitionsJsonFile File);
