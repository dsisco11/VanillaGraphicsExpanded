#version 330 core

// Placeholder normal+depth atlas baker (plumbing stage).
// Output encoding (provisional): RGBA = (normalXYZ_packed01, depth01)

in vec2 v_uv;
out vec4 outColor;

// Base albedo atlas page (optional for placeholder stage).
uniform sampler2D baseAlbedoAtlas;

// 0 = constant depth (0)
// 1 = use luminance(baseAlbedoAtlas) as depth
uniform int vge_useLuminanceDepth;

void main()
{
    // Constant tangent-space-ish "flat" normal encoded to [0,1]
    vec3 n01 = vec3(0.5, 0.5, 1.0);
    float depth01 = 0.0;

    if (vge_useLuminanceDepth != 0)
    {
        vec3 albedo = texture(baseAlbedoAtlas, v_uv).rgb;
        // Standard luminance weights
        depth01 = dot(albedo, vec3(0.2126, 0.7152, 0.0722));
    }

    outColor = vec4(n01, depth01);
}
