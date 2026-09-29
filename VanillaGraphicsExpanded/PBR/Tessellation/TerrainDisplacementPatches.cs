using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.PBR.Tessellation;

/// <summary>Declares the engine-visible displacement interface and shadow geometry outputs during source patching.</summary>
internal static class TerrainDisplacementPatches
{
    #region Source preparation
    /// <summary>Exposes stage uniforms to the engine's ordinary uniform collection and prepares shadow varyings.</summary>
    internal static bool Apply(SyntaxTree tree, string name)
    {
        var editor = tree.CreateEditor();
        if (!Apply(editor, name)) return false;
        editor.Commit();
        return true;
    }

    /// <summary>Queues displacement interface edits into a stage-scoped transaction.</summary>
    internal static bool Apply(SyntaxEditor editor, string name)
    {
        if (name is not ("chunkopaque.vsh" or "chunktopsoil.vsh" or "chunkshadowmap.vsh")) return false;
        var main = Query.Syntax<GlFunctionNode>().Named("main");
        editor.InsertBefore(main, """
            uniform sampler2D vge_displacementTex;
            uniform sampler2D vge_displacementRecords;
            uniform sampler2D vge_normalDepthTex;
            uniform vec4 vge_tessellationPixels;
            uniform vec2 vge_tessellationDistance;
            uniform float vge_tessellationFocalPixels;
            uniform int vge_displacementEnabled;
            uniform int vge_displacementReactive;

            """);
        if (name != "chunkshadowmap.vsh")
        {
            editor.InsertBefore(main, """
                out vec4 vge_surfaceBasePosition;
                out vec3 vge_surfaceBaseNormal;
                out float vge_surfaceDisplaced;

                """).InsertBefore(main.InnerEnd("body"), """

                vge_surfaceBasePosition = worldPos;
                vge_surfaceBaseNormal = normal;
                vge_surfaceDisplaced = 0.0;
                """);
            return true;
        }
        // Keep the engine's local worldPos and export it under a distinct name.
        editor.InsertBefore(main, """
            out vec4 vge_shadowPosition;
            out vec3 normal;
            flat out int renderFlags;
            flat out vec2 vge_uvBase;
            flat out vec2 vge_uvExtent;

            """).InsertBefore(main.InnerEnd("body"), """

            vge_shadowPosition = worldPos;
            #if USESSBO > 0
            renderFlags = vdata.flags[vIndex];
            #else
            renderFlags = renderFlagsIn;
            #endif
            normal = unpackNormal(renderFlags);
            vge_uvBase = vec2(-1.0);
            vge_uvExtent = vec2(0.0);
            """);
        return true;
    }
    #endregion
}
