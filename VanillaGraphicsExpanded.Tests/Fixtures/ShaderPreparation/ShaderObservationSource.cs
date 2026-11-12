using TinyTokenizer.Ast;
using VanillaGraphicsExpanded;

namespace VgeShaderTestPreparation;

/// <summary>Builds a feedback-only observation stage from an expanded shadow producer using typed GLSL syntax nodes.</summary>
public static class ShaderObservationSource
{
    #region Public API
    /// <summary>Replaces the producer entry body and attaches transform-feedback layout to its unique position output.</summary>
    public static string Capture(string source)
    {
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        var outputs = tree.Select(Query.Syntax<GlStageIoNode>())
            .Cast<GlStageIoNode>()
            .Where(static node => node.Name == "vge_shadowPosition")
            .ToArray();
        if (outputs.Length != 1 || outputs[0].Storage != GlInterfaceStorage.Output ||
            outputs[0].ValueType != "vec4" || outputs[0].Children.OfType<SyntaxBlock>().Any())
            throw new InvalidDataException("Position observation requires one scalar vec4 stage output named vge_shadowPosition.");

        var functions = tree.Select(Query.Syntax<GlFunctionNode>()).Cast<GlFunctionNode>().ToArray();
        var entryPoints = functions.Where(static node => node.Name == "main").ToArray();
        if (entryPoints.Length != 1 || !functions.Any(static node => node.Name == "vgeShadowMaterialMain"))
            throw new InvalidDataException("Position observation requires one main entry point and a generated material function.");

        // Modify only the parsed entry body and declaration node, leaving comments and unrelated identifiers untouched.
        tree.CreateEditor()
            .Replace(Query.Syntax<GlFunctionNode>().Named("main").Block("body").Inner(),
                "\nvgeShadowMaterialMain();\ngl_Position = vge_shadowPosition;\n")
            .InsertBefore(outputs[0], "layout(xfb_buffer=0, xfb_offset=0, xfb_stride=16) ")
            .Commit();
        return tree.ToText();
    }
    #endregion
}
