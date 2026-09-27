using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Replaces the engine sky lookup while retaining its night alpha, underwater effects and separate celestial passes.</summary>
internal static class AtmosphereSkyPatches
{
    #region Source patches
    /// <summary>Imports scene-linear display conversion before source expansion.</summary>
    internal static void Preprocess(SyntaxTree tree) => tree.CreateEditor()
        .InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"), """
        @import "./includes/pbr_color.glsl"

        """).Commit();

    /// <summary>Replaces sky RGB after engine alpha evaluation; the lookup is initialized before the first scene draw.</summary>
    internal static void Apply(SyntaxTree tree)
    {
        tree.CreateEditor()
            .InsertBefore(Query.Syntax<GlFunctionNode>().Named("getSkyColorAt"),
                """
                uniform sampler2D vge_atmosphereSky;
                vec3 VgeResolveDisplay(vec3 radiance);

                """)
            .InsertBefore(Query.Syntax<GlFunctionNode>().Named("getSkyColorAt").InnerEnd("body"), """

                {
                    vec3 direction = normalize(skyPosition);
                    float azimuth = dot(direction.xz, direction.xz) > 0.0000001 ? atan(direction.z, direction.x) : 0.0;
                    vec2 lookupUv = vec2(azimuth / 6.28318530718,
                        asin(clamp(direction.y, -1.0, 1.0)) / 3.14159265359 + 0.5);
                    vec3 radiance = texture(vge_atmosphereSky, lookupUv).rgb;
                    // Stars are rendered before the dome. Retain the engine's twilight alpha policy.
                    skyColor.rgb = VgeResolveDisplay(radiance);
                    skyGlow = vec4(0.0, 0.0, 0.0, 1.0);
                }
                """).Commit();
    }
    #endregion
}
