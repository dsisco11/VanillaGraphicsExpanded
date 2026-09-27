using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Replaces only the scoped engine solar draw while retaining the standard shader for mesh rendering.</summary>
internal static class AtmosphereSunPatches
{
    #region Shader integration
    /// <summary>Imports stage-specific solar geometry or radiance before engine include expansion.</summary>
    internal static void Preprocess(SyntaxTree tree, string name)
    {
        string stage = name == "standard.vsh" ? "vertex" : "fragment";
        tree.CreateEditor().InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"), $"""
            @import "./includes/atmosphere_sun_{stage}.glsl"

            """).Commit();
    }

    /// <summary>Branches before texture, material, fog or warp evaluation only for the engine sun callback.</summary>
    internal static void Apply(SyntaxTree tree)
    {
        tree.CreateEditor().InsertAfter(Query.Syntax<GlFunctionNode>().Named("main").InnerStart("body"), """

            if (vge_atmosphereSunDraw != 0)
            {
                VgeDrawAtmosphericSun();
                return;
            }

            """).Commit();
    }
    #endregion
}
