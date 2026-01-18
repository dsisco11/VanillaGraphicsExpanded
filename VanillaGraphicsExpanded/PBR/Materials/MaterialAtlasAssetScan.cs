using System;
using System.Collections.Generic;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Helper utilities for producing deterministic asset lists for planning.
/// The actual scanning (e.g. from <c>capi.Assets</c>) should happen outside the planner;
/// this class is used to normalize/sort/dedupe those inputs.
/// </summary>
internal static class MaterialAtlasAssetScan
{
    public static IReadOnlyList<AssetLocation> NormalizeSortAndDedupeBlockTextures(IEnumerable<AssetLocation> locations)
    {
        ArgumentNullException.ThrowIfNull(locations);

        var set = new HashSet<AssetLocation>(AssetLocationComparer.Instance);
        foreach (AssetLocation loc in locations)
        {
            if (loc is null)
            {
                continue;
            }

            // Planner convention: normalizing texture assets to file-like keys here would be surprising;
            // keep raw asset locations and let the atlas position resolver normalize to atlas keys.
            set.Add(loc);
        }

        var list = new List<AssetLocation>(set);
        list.Sort(AssetLocationComparer.Instance);
        return list;
    }

    private sealed class AssetLocationComparer : IComparer<AssetLocation>, IEqualityComparer<AssetLocation>
    {
        public static AssetLocationComparer Instance { get; } = new();

        public int Compare(AssetLocation? x, AssetLocation? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            int d = string.Compare(x.Domain, y.Domain, StringComparison.Ordinal);
            if (d != 0) return d;

            return string.Compare(x.Path, y.Path, StringComparison.Ordinal);
        }

        public bool Equals(AssetLocation? x, AssetLocation? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;

            return string.Equals(x.Domain, y.Domain, StringComparison.Ordinal)
                && string.Equals(x.Path, y.Path, StringComparison.Ordinal);
        }

        public int GetHashCode(AssetLocation obj)
            => HashCode.Combine(obj.Domain, obj.Path);
    }
}
