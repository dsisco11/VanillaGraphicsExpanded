using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains array-shaped resource inputs without borrowing mutable caller storage.</summary>
internal static class ShaderInputSnapshot
{
    #region Public API
    /// <summary>Compares desired array values so stable submissions do not repeatedly clone their storage.</summary>
    internal static bool Equal<T>(T[]? left, T[]? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.Length != right.Length) return false;
        for (int index = 0; index < left.Length; index++)
            if (!System.Collections.Generic.EqualityComparer<T>.Default.Equals(left[index], right[index])) return false;
        return true;
    }
    /// <summary>Copies an input array while preserving the borrowed identities of its resources.</summary>
    internal static T Copy<T>(T value) => value is Array array ? (T)(object)array.Clone() : value;
    #endregion
}
