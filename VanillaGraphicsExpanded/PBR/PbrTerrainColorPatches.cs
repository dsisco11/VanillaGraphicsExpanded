using System;
using System.Linq;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Separates opaque terrain material color from the engine's forward lighting.</summary>
internal static class PbrTerrainColorPatches
{
    #region Material capture

    /// <summary>Preserves vertex alpha and lighting side outputs while removing RGB light modulation.</summary>
    internal static void ApplyVertex(SyntaxTree tree, string sourceName)
    {
        if (sourceName is not ("chunkopaque.vsh" or "chunktopsoil.vsh")) return;
        tree.CreateEditor().InsertBefore(
            Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"),
            "\n    // Deferred lighting consumes unlit material RGB; retain the engine fade alpha.\n    rgba.rgb = vec3(1.0);\n").Commit();
    }

    /// <summary>Captures color-mapped terrain before forward effects without changing coverage or glow outputs.</summary>
    internal static void ApplyFragment(SyntaxTree tree, string sourceName)
    {
        if (sourceName is not ("chunkopaque.fsh" or "chunktopsoil.fsh")) return;

        // Both installed shaders enter their forward-only effects at this declaration.
        // Fail on a changed engine layout rather than silently publishing lit material color.
        var main = tree.Select(Query.Syntax<GlFunctionNode>().Named("main")).OfType<GlFunctionNode>().Single();
        var children = main.Body.Children.ToArray();
        SyntaxNode? boundary = null;
        for (int index = 0; index + 1 < children.Length; index++)
        {
            if (children[index] is not SyntaxToken { Text: "float" }
                || children[index + 1] is not SyntaxToken { Text: "murkiness" }) continue;
            if (boundary is not null)
                throw new InvalidOperationException($"Ambiguous material capture boundary in {sourceName}.");
            boundary = children[index];
        }
        if (boundary is null)
            throw new InvalidOperationException($"Unsupported material capture boundary in {sourceName}.");
        string color = sourceName == "chunkopaque.fsh" ? "texColor.rgb" : "outColor.rgb";
        // Restore after the complete body, independently of its final output statement.
        tree.CreateEditor()
            .InsertBefore(boundary, $"vec3 vge_materialColor = VgeSrgbToLinear({color});\n    ")
            .InsertBefore(Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"),
                "\n#if NORMALVIEW == 0\n    outColor.rgb = vge_materialColor;\n#endif\n")
            .Commit();
    }

    #endregion
}
