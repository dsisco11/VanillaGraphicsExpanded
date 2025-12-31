using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using TinyTokenizer.Ast;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Processes <c>@import</c> directives in shader source code using TinyAst.
/// </summary>
/// <remarks>
/// This processor finds all <c>@import</c> directives (matched as <see cref="GlDirectiveNode"/>)
/// and replaces them with the referenced file contents, commenting out the original directive for debugging.
/// </remarks>
public static class SourceCodeImportsProcessor
{
    /// <summary>
    /// Processes all @import directives in the given SyntaxTree by inlining imported content.
    /// </summary>
    /// <param name="tree">The SyntaxTree to process.</param>
    /// <param name="importsCache">Dictionary mapping import file names to their contents.</param>
    /// <param name="logger">Optional logger for audit messages.</param>
    /// <returns>True if any imports were processed, false if no imports found.</returns>
    /// <exception cref="InvalidOperationException">Thrown if an imported file is not found in the cache.</exception>
    public static bool ProcessImports(SyntaxTree tree, IReadOnlyDictionary<string, string> importsCache, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(importsCache);

        // Find all @import directives using the GlslDirectiveSyntax
        var imports = tree.Select(Query.Syntax<GlImportNode>()).Cast<GlImportNode>().ToList();

        if (imports.Count == 0)
        {
            return false;
        }

        var editor = tree.CreateEditor();

        foreach (GlImportNode import in imports)
        {
            // Extract the filename from the arguments (should be a string like "filename.glsl")
            var fileName = import.ImportString;
            if (string.IsNullOrEmpty(fileName))
            {
                logger?.Warning($"[VGE] Could not extract filename from @import directive at position {import.Position}");
                continue;
            }

            if (!importsCache.TryGetValue(fileName, out var importContent))
            {
                throw new InvalidOperationException($"Import file '{fileName}' not found in imports cache");
            }

            // Build the replacement: commented original line + newline + import contents
            var originalText = import.ToString();
            var injection = new StringBuilder();
            injection.Append("//");  // Comment out the @import line
            injection.Append(originalText.TrimEnd());
            injection.AppendLine();
            injection.Append(importContent);

            // Ensure import content ends with newline
            if (!importContent.EndsWith('\n'))
            {
                injection.AppendLine();
            }

            editor.Replace(import, injection.ToString());

            logger?.Audit($"[VGE] Processed @import '{fileName}' at position {import.Position}");
        }

        editor.Commit();
        return true;
    }
}
