using System.Linq;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Connects installed surface material entry points to the authored shared deformation model.</summary>
internal static class VertexDeformationPatches
{
    #region Public API
    /// <summary>Limits replacement to geometry families whose shadow inputs are supported.</summary>
    internal static bool Supports(string name) => name is "chunkopaque.vsh" or "chunktopsoil.vsh"
        or "chunktransparent.vsh" or "chunkshadowmap.vsh" or "entityanimated.vsh"
        or "shadowmapentityanimated.vsh" or "standard.vsh" or "instanced.vsh";

    /// <summary>Imports authored functions through the existing surface shader preprocessing owner.</summary>
    internal static bool Preprocess(SyntaxTree tree, string sourceName)
    {
        if (!Supports(sourceName) || !HasDeformation(tree)) return false;
        // Definitions precede the native wrappers that call them. Native material
        // uniforms are declared before its first deformation function.
        var first = tree.Select(Query.Syntax<GlFunctionNode>()).OfType<GlFunctionNode>()
            .First(node => node.Name is "applyPerceptionWarping" or "applyLiquidWarping" or "applyVertexWarping" or "applyGlobalWarping");
        tree.CreateEditor().InsertBefore(Query.Syntax<GlFunctionNode>().Named(first.Name),
            """
            #define VGE_VERTEX_DEFORMATION_ENABLED WAVINGSTUFF
            @import "vanillagraphicsexpanded:shaders/includes/deformation/vertex-deformation.glsl"
            /** Adapts native frame uniforms only at the installed surface boundary. */
            VgeVertexDeformationInputs VgeVisibleDeformationInputs() {
                return VgeVertexDeformationInputs(playerpos, timeCounter, windWaveCounter,
                    windWaveCounterHighFreq, waterWaveCounter, windSpeed, globalWarpIntensity,
                    glitchWaviness, windWaveIntensity, waterWaveIntensity,
                    perceptionEffectId, perceptionEffectIntensity);
            }

            """).Commit();
        return true;
    }

    /// <summary>Preserves native call sites and parameter conventions while replacing their mathematical implementation.</summary>
    internal static bool Apply(SyntaxTree tree, string sourceName)
    {
        if (!Supports(sourceName) || !HasDeformation(tree)) return false;
        var editor = tree.CreateEditor();
        // The original call sites retain their material-specific order, skinning,
        // instance placement and quarter-strength item behavior. Only deformation
        // bodies change, so visible and shadow producers evaluate the same model.
        Replace("applyVertexWarping", "return VgeVertexDeform(VgeVisibleDeformationInputs(), renderFlags, worldPos);");
        Replace("applyLiquidWarping", "return VgeLiquidDeform(VgeVisibleDeformationInputs(), windAffected, worldPos, div);");
        Replace("applyGlobalWarping", "return VgeGlobalDeform(VgeVisibleDeformationInputs(), worldPos);");
        Replace("applyPerceptionWarping", "return VgePerceptionDeform(VgeVisibleDeformationInputs(), worldPos);");
        editor.Commit();
        return true;

        // Replace only functions present in this material's expanded native source.
        void Replace(string name, string body)
        {
            if (tree.Select(Query.Syntax<GlFunctionNode>().Named(name)).Any())
                editor.Replace(Query.Syntax<GlFunctionNode>().Named(name).Block("body").Inner(), "\n" + body + "\n");
        }
    }
    #endregion

    #region Private
    /// <summary>A material without the native deformation interface needs no imported uniforms or functions.</summary>
    private static bool HasDeformation(SyntaxTree tree) =>
        tree.Select(Query.Syntax<GlFunctionNode>().Named("applyVertexWarping")).Any();
    #endregion
}
