using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains array-shaped resource inputs without borrowing mutable caller storage.</summary>
internal static class ShaderInputSnapshot
{
    #region Public API
    /// <summary>Copies an input array while preserving the borrowed identities of its resources.</summary>
    internal static T Copy<T>(T value) => value is Array array ? (T)(object)array.Clone() : value;
    #endregion
}
