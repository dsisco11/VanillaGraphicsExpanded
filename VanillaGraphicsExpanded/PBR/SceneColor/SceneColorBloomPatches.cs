using System;
using System.Linq;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Defines bloom kernel spacing in the pixels of each downsampled destination.</summary>
internal static class SceneColorBloomPatches
{
    #region Public API
    /// <summary>Preserves the installed Gaussian weights while making both blur axes use the same pixel scale.</summary>
    internal static void Apply(SyntaxTree tree)
    {
        var main = tree.Select(Query.Syntax<GlFunctionNode>().Named("main")).OfType<GlFunctionNode>().Single();
        string body = main.ToText();
        // The engine supplies full-window frameSize even for reduced bloom targets.
        // Derivatives of the center UV measure the actual destination pixel footprint,
        // so horizontal and vertical passes remain symmetric across downsampling.
        // Construct every tap from that center; the engine vertex leaves tap 16 unset.
        for (int tap = 0; tap <= 16; tap++)
        {
            string sample = $"texture(inputTexture, texCoords[{tap}])";
            if (!body.Contains(sample, StringComparison.Ordinal))
                throw new InvalidOperationException($"Missing bloom kernel tap {tap}.");
            body = body.Replace(sample,
                $"texture(inputTexture, texCoords[8] + vge_bloomPixelStep * {tap - 8}.0)", StringComparison.Ordinal);
        }
        int start = body.IndexOf('{');
        body = body.Insert(start + 1,
            "\n    vec2 vge_bloomPixelStep = isVertical == 1 ? dFdy(texCoords[8]) : dFdx(texCoords[8]);\n");
        tree.CreateEditor().Replace(main, "uniform int isVertical;\n" + body).Commit();
    }
    #endregion
}
