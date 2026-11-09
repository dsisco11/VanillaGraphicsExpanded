using System.Text.RegularExpressions;

namespace VanillaGraphicsExpanded.Tests.Support.Terrain;

/// <summary>Expands relative shader imports from installed test assets.</summary>
internal static class TerrainShaderSourceExpander
{
    #region Public API
    /// <summary>Expands each relative <c>@import</c> recursively while preserving imported source text.</summary>
    /// <param name="path">The installed shader source file to expand.</param>
    /// <returns>The source text with relative imports replaced by their contents.</returns>
    internal static string ExpandRelativeImports(string path)
    {
        // Resolve each import relative to the file that declares it so nested shader includes retain their paths.
        return Regex.Replace(TestAssetSnapshot.ReadSourceText(path), "@import\\s+\"([^\"]+)\"",
            match => ExpandRelativeImports(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, match.Groups[1].Value))));
    }
    #endregion
}
