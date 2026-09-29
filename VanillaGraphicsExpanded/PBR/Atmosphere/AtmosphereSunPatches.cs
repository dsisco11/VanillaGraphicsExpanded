using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Replaces only the scoped engine solar draw while retaining the standard shader for mesh rendering.</summary>
internal static class AtmosphereSunPatches
{
    #region Shader integration
    /// <summary>Imports stage-specific solar geometry or radiance before engine include expansion.</summary>
    internal static void Preprocess(SyntaxTree tree, string name)
    {
        var editor = tree.CreateEditor();
        Preprocess(editor, name);
        editor.Commit();
    }

    /// <summary>Queues stage-specific solar imports into a stage-scoped transaction.</summary>
    internal static void Preprocess(SyntaxEditor editor, string name)
    {
        string stage = name == "standard.vsh" ? "vertex" : "fragment";
        editor.InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"), $"""
            @import "./includes/atmosphere_sun_{stage}.glsl"

            """);
    }

    /// <summary>Branches before texture, material, fog or warp evaluation only for the engine sun callback.</summary>
    internal static void Apply(SyntaxTree tree)
    {
        var editor = tree.CreateEditor();
        Apply(editor);
        editor.Commit();
    }

    /// <summary>Queues the scoped atmospheric sun branch into a stage transaction.</summary>
    internal static void Apply(SyntaxEditor editor)
    {
        editor.InsertAfter(Query.Syntax<GlFunctionNode>().Named("main").InnerStart("body"), """

            if (vge_atmosphereSunDraw != 0)
            {
                VgeDrawAtmosphericSun();
                return;
            }

            """);
    }
    #endregion
}
