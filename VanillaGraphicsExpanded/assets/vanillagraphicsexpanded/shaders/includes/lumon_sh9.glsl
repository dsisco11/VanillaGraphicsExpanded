// LumOn SH9 (3-band) helpers.
// Real SH basis for l<=2 (9 coefficients) and Lambertian diffuse convolution.
//
// Coefficient convention:
//   c_lm = ∫ L(ω) Y_lm(ω) dω
// Diffuse irradiance for normal n:
//   E(n) = Σ_l A_l Σ_m c_lm Y_lm(n)
// Outgoing diffuse (Lambertian) = E(n) / π

#ifndef VGE_LUMON_SH9_GLSL
#define VGE_LUMON_SH9_GLSL

@import "./lumon_sh9_common.glsl"

// ============================================================================
// SH9 Basis Functions
// ============================================================================

// Returns basis split as:
//   b0 = (Y00, Y1-1, Y10, Y11)
//   b1 = (Y2-2, Y2-1, Y20, Y21)
//   b8 = Y22
void lumonSH9Basis(vec3 dir, out vec4 b0, out vec4 b1, out float b8)
{
    float x = dir.x;
    float y = dir.y;
    float z = dir.z;

    b0 = vec4(
        SH9_C0,
        SH9_C1 * y,
        SH9_C1 * z,
        SH9_C1 * x
    );

    b1 = vec4(
        SH9_C2 * x * y,
        SH9_C2 * y * z,
        SH9_C3 * (3.0 * z * z - 1.0),
        SH9_C2 * x * z
    );

    b8 = SH9_C4 * (x * x - y * y);
}

/// Compute SH9 basis for a direction (struct version).
LumOnSH9 lumonSH9BasisFunction(vec3 dir)
{
    LumOnSH9 sh;
    float b8;
    lumonSH9Basis(dir, sh.v0, sh.v1, b8);
    sh.v2 = b8;
    return sh;
}

// Legacy projection accumulator (raw vec3 coefficients).
// Kept for compatibility with existing shaders.
void lumonSH9ProjectAccumulate(
    inout vec3 c0, inout vec3 c1, inout vec3 c2, inout vec3 c3,
    inout vec3 c4, inout vec3 c5, inout vec3 c6, inout vec3 c7, inout vec3 c8,
    vec3 dir,
    vec3 radiance,
    float weight)
{
    vec4 b0, b1;
    float b8;
    lumonSH9Basis(dir, b0, b1, b8);

    c0 += radiance * (b0.x * weight);
    c1 += radiance * (b0.y * weight);
    c2 += radiance * (b0.z * weight);
    c3 += radiance * (b0.w * weight);

    c4 += radiance * (b1.x * weight);
    c5 += radiance * (b1.y * weight);
    c6 += radiance * (b1.z * weight);
    c7 += radiance * (b1.w * weight);

    c8 += radiance * (b8 * weight);
}

/// Project radiance into SH9RGB (struct version).
/// Accumulates weighted radiance × basis into sh.
void lumonSH9RGBProjectAccumulate(inout LumOnSH9RGB sh, vec3 dir, vec3 radiance, float weight)
{
    LumOnSH9 basis = lumonSH9BasisFunction(dir);
    LumOnSH9RGB weighted = lumonSH9MulColor(basis, radiance * weight);
    sh = lumonSH9RGBAdd(sh, weighted);
}

// Packs 9 RGB coefficients into 7 vec4s (27 floats -> 28 slots).
// Layout:
//  o0 = (c0.r,c0.g,c0.b,c1.r)
//  o1 = (c1.g,c1.b,c2.r,c2.g)
//  o2 = (c2.b,c3.r,c3.g,c3.b)
//  o3 = (c4.r,c4.g,c4.b,c5.r)
//  o4 = (c5.g,c5.b,c6.r,c6.g)
//  o5 = (c6.b,c7.r,c7.g,c7.b)
//  o6 = (c8.r,c8.g,c8.b,0)
void lumonSH9Pack(
    vec3 c0, vec3 c1, vec3 c2, vec3 c3,
    vec3 c4, vec3 c5, vec3 c6, vec3 c7, vec3 c8,
    out vec4 o0, out vec4 o1, out vec4 o2, out vec4 o3, out vec4 o4, out vec4 o5, out vec4 o6)
{
    o0 = vec4(c0.rgb, c1.r);
    o1 = vec4(c1.g, c1.b, c2.r, c2.g);
    o2 = vec4(c2.b, c3.r, c3.g, c3.b);

    o3 = vec4(c4.rgb, c5.r);
    o4 = vec4(c5.g, c5.b, c6.r, c6.g);
    o5 = vec4(c6.b, c7.r, c7.g, c7.b);

    o6 = vec4(c8.rgb, 0.0);
}

/// Pack SH9RGB struct into 7 vec4s.
void lumonSH9RGBPack(LumOnSH9RGB sh, out vec4 o0, out vec4 o1, out vec4 o2, out vec4 o3, out vec4 o4, out vec4 o5, out vec4 o6)
{
    vec3 c0 = vec3(sh.r.v0.x, sh.g.v0.x, sh.b.v0.x);
    vec3 c1 = vec3(sh.r.v0.y, sh.g.v0.y, sh.b.v0.y);
    vec3 c2 = vec3(sh.r.v0.z, sh.g.v0.z, sh.b.v0.z);
    vec3 c3 = vec3(sh.r.v0.w, sh.g.v0.w, sh.b.v0.w);
    vec3 c4 = vec3(sh.r.v1.x, sh.g.v1.x, sh.b.v1.x);
    vec3 c5 = vec3(sh.r.v1.y, sh.g.v1.y, sh.b.v1.y);
    vec3 c6 = vec3(sh.r.v1.z, sh.g.v1.z, sh.b.v1.z);
    vec3 c7 = vec3(sh.r.v1.w, sh.g.v1.w, sh.b.v1.w);
    vec3 c8 = vec3(sh.r.v2, sh.g.v2, sh.b.v2);

    lumonSH9Pack(c0, c1, c2, c3, c4, c5, c6, c7, c8, o0, o1, o2, o3, o4, o5, o6);
}

void lumonSH9Unpack(
    vec4 t0, vec4 t1, vec4 t2, vec4 t3, vec4 t4, vec4 t5, vec4 t6,
    out vec3 c0, out vec3 c1, out vec3 c2, out vec3 c3,
    out vec3 c4, out vec3 c5, out vec3 c6, out vec3 c7, out vec3 c8)
{
    c0 = t0.rgb;
    c1 = vec3(t0.a, t1.r, t1.g);
    c2 = vec3(t1.b, t1.a, t2.r);
    c3 = vec3(t2.g, t2.b, t2.a);

    c4 = t3.rgb;
    c5 = vec3(t3.a, t4.r, t4.g);
    c6 = vec3(t4.b, t4.a, t5.r);
    c7 = vec3(t5.g, t5.b, t5.a);

    c8 = t6.rgb;
}

/// Unpack 7 vec4s into SH9RGB struct.
LumOnSH9RGB lumonSH9RGBUnpack(vec4 t0, vec4 t1, vec4 t2, vec4 t3, vec4 t4, vec4 t5, vec4 t6)
{
    vec3 c0, c1, c2, c3, c4, c5, c6, c7, c8;
    lumonSH9Unpack(t0, t1, t2, t3, t4, t5, t6, c0, c1, c2, c3, c4, c5, c6, c7, c8);

    LumOnSH9RGB sh;
    sh.r.v0 = vec4(c0.r, c1.r, c2.r, c3.r);
    sh.r.v1 = vec4(c4.r, c5.r, c6.r, c7.r);
    sh.r.v2 = c8.r;

    sh.g.v0 = vec4(c0.g, c1.g, c2.g, c3.g);
    sh.g.v1 = vec4(c4.g, c5.g, c6.g, c7.g);
    sh.g.v2 = c8.g;

    sh.b.v0 = vec4(c0.b, c1.b, c2.b, c3.b);
    sh.b.v1 = vec4(c4.b, c5.b, c6.b, c7.b);
    sh.b.v2 = c8.b;

    return sh;
}

vec3 lumonSH9EvaluateDiffuse(
    vec3 c0, vec3 c1, vec3 c2, vec3 c3,
    vec3 c4, vec3 c5, vec3 c6, vec3 c7, vec3 c8,
    vec3 normal)
{
    vec4 b0, b1;
    float b8;
    lumonSH9Basis(normalize(normal), b0, b1, b8);

    // Apply cosine-kernel band weights (l=0..2)
    vec3 irradiance = vec3(0.0);

    irradiance += c0 * (b0.x * SH9_A0);

    irradiance += c1 * (b0.y * SH9_A1);
    irradiance += c2 * (b0.z * SH9_A1);
    irradiance += c3 * (b0.w * SH9_A1);

    irradiance += c4 * (b1.x * SH9_A2);
    irradiance += c5 * (b1.y * SH9_A2);
    irradiance += c6 * (b1.z * SH9_A2);
    irradiance += c7 * (b1.w * SH9_A2);
    irradiance += c8 * (b8 * SH9_A2);

    // Convert irradiance to outgoing diffuse by dividing by π
    return max(vec3(0.0), irradiance) / LUMON_SH9_PI;
}

/// Evaluate diffuse irradiance from SH9RGB struct.
vec3 lumonSH9RGBEvaluateDiffuse(LumOnSH9RGB sh, vec3 normal)
{
    LumOnSH9 basis = lumonSH9BasisFunction(normalize(normal));

    // Dot product with band weights
    vec3 irradiance = vec3(0.0);
    irradiance.r = dot(sh.r.v0, basis.v0 * SH9_A0) + dot(sh.r.v0.yzw, basis.v0.yzw * SH9_A1);
    irradiance.r += dot(sh.r.v1, basis.v1 * SH9_A2) + sh.r.v2 * basis.v2 * SH9_A2;

    irradiance.g = dot(sh.g.v0, basis.v0 * SH9_A0) + dot(sh.g.v0.yzw, basis.v0.yzw * SH9_A1);
    irradiance.g += dot(sh.g.v1, basis.v1 * SH9_A2) + sh.g.v2 * basis.v2 * SH9_A2;

    irradiance.b = dot(sh.b.v0, basis.v0 * SH9_A0) + dot(sh.b.v0.yzw, basis.v0.yzw * SH9_A1);
    irradiance.b += dot(sh.b.v1, basis.v1 * SH9_A2) + sh.b.v2 * basis.v2 * SH9_A2;

    return max(vec3(0.0), irradiance) / LUMON_SH9_PI;
}

vec3 lumonSH9EvaluateDiffusePacked(vec4 t0, vec4 t1, vec4 t2, vec4 t3, vec4 t4, vec4 t5, vec4 t6, vec3 normal)
{
    vec3 c0, c1, c2, c3, c4, c5, c6, c7, c8;
    lumonSH9Unpack(t0, t1, t2, t3, t4, t5, t6, c0, c1, c2, c3, c4, c5, c6, c7, c8);
    return lumonSH9EvaluateDiffuse(c0, c1, c2, c3, c4, c5, c6, c7, c8, normal);
}

#endif
