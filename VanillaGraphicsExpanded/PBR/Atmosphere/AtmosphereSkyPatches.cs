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
        @import "./includes/atmosphere_sky_mapping.glsl"

        """).Commit();

    /// <summary>Replaces sky RGB after engine alpha evaluation; the lookup is initialized before the first scene draw.</summary>
    internal static void Apply(SyntaxTree tree)
    {
        tree.CreateEditor()
            .InsertBefore(Query.Syntax<GlFunctionNode>().Named("getSkyColorAt"),
                """
                uniform sampler2D vge_atmosphereSky;
                uniform float vge_atmosphereLutHorizon;
                vec3 VgeResolveDisplay(vec3 radiance);
                float atmSkyCoordinate(float elevation, float horizon);

                """)
            .InsertBefore(Query.Syntax<GlFunctionNode>().Named("getSkyColorAt").InnerEnd("body"), """

                {
                    vec3 direction = normalize(skyPosition);
                    float azimuth = dot(direction.xz, direction.xz) > 0.0000001 ? atan(direction.z, direction.x) : 0.0;
                    float row = atmSkyCoordinate(asin(clamp(direction.y, -1.0, 1.0)), vge_atmosphereLutHorizon);
                    float rows = float(textureSize(vge_atmosphereSky, 0).y);
                    vec2 lookupUv = vec2(azimuth / 6.28318530718, (row * (rows - 1.0) + 0.5) / rows);
                    vec3 radiance = texture(vge_atmosphereSky, lookupUv).rgb;
                    // Stars are rendered before the dome. Retain the engine's twilight alpha policy.
                    skyColor.rgb = VgeResolveDisplay(radiance);
                    skyGlow = vec4(0.0, 0.0, 0.0, 1.0);
                }
                """)
            .InsertBefore(Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"), """

                // Dither after underwater/night-vision display effects, immediately before primary RGBA8 storage.
                outColor.rgb = VgeDitherDisplay(outColor.rgb, gl_FragCoord.xy);

                """).Commit();
    }
    #endregion
}
