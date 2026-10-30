using System.Collections.Generic;
using System.Collections.Immutable;
namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Provides value semantics for small canonical metadata collections.</summary>
internal static class MetadataCollections
{
    #region Internal API
    /// <summary>Compares unordered values, preserving default versus known-empty and duplicate multiplicity.</summary>
    internal static bool Equal<T>(ImmutableArray<T> left, ImmutableArray<T> right) where T : struct
    {
        if (left.IsDefault || right.IsDefault) return left.IsDefault == right.IsDefault;
        if (left.Length != right.Length) return false;
        bool sameOrder = true;
        for (int i = 0; i < left.Length; i++)
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i])) { sameOrder = false; break; }
        if (sameOrder) return true;
        // Canonical keys are unique; counting also handles malformed duplicate inputs deterministically.
        var comparer = EqualityComparer<T>.Default;
        foreach (var item in left)
        {
            int count = 0;
            foreach (var value in left) if (comparer.Equals(item, value)) count++;
            foreach (var value in right) if (comparer.Equals(item, value)) count--;
            if (count != 0) return false;
        }
        return true;
    }
    /// <summary>Hashes unordered values with a distinct default marker.</summary>
    internal static int Hash<T>(ImmutableArray<T> values) where T : struct
    {
        if (values.IsDefault) return -1;
        int hash = values.Length;
        foreach (var value in values) hash = unchecked(hash + value.GetHashCode());
        return hash;
    }
    #endregion
}
