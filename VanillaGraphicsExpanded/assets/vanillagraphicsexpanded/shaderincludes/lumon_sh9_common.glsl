// LumOn SH9 Common Types and Operations
// Defines fundamental SH9 data structures and arithmetic operations.

#ifndef VGE_LUMON_SH9_COMMON_GLSL
#define VGE_LUMON_SH9_COMMON_GLSL

// ============================================================================
// Constants
// ============================================================================

const float LUMON_SH9_PI = 3.141592654;

// Real SH basis constants (normalized)
const float SH9_C0 = 0.282095;   // 1/(2*sqrt(pi))
const float SH9_C1 = 0.488603;   // sqrt(3)/(2*sqrt(pi))
const float SH9_C2 = 1.092548;   // sqrt(15)/(2*sqrt(pi))
const float SH9_C3 = 0.315392;   // sqrt(5)/(4*sqrt(pi))
const float SH9_C4 = 0.546274;   // sqrt(15)/(4*sqrt(pi))

// Lambertian cosine-kernel band factors for l=0..2
const float SH9_A0 = 3.141592654;  // π
const float SH9_A1 = 2.094395102;  // 2π/3
const float SH9_A2 = 0.785398163;  // π/4

// ============================================================================
// Data Structures
// ============================================================================

/// SH9 coefficients for a scalar (single-channel) function.
/// 9 floats: 1 DC (l=0) + 3 linear (l=1) + 5 quadratic (l=2).
struct LumOnSH9
{
    vec4 v0;  // Y00, Y1-1, Y10, Y11
    vec4 v1;  // Y2-2, Y2-1, Y20, Y21
    float v2; // Y22
};

/// SH9 coefficients for an RGB (three-channel) function.
/// 27 floats total: 3 × LumOnSH9.
struct LumOnSH9RGB
{
    LumOnSH9 r;
    LumOnSH9 g;
    LumOnSH9 b;
};

// ============================================================================
// Initialization
// ============================================================================

/// Initialize scalar SH9 to zero.
LumOnSH9 lumonSH9Zero()
{
    LumOnSH9 sh;
    sh.v0 = vec4(0.0);
    sh.v1 = vec4(0.0);
    sh.v2 = 0.0;
    return sh;
}

/// Initialize RGB SH9 to zero.
LumOnSH9RGB lumonSH9RGBZero()
{
    LumOnSH9RGB sh;
    sh.r = lumonSH9Zero();
    sh.g = lumonSH9Zero();
    sh.b = lumonSH9Zero();
    return sh;
}

// ============================================================================
// Arithmetic Operations
// ============================================================================

/// Add two scalar SH9 structures.
LumOnSH9 lumonSH9Add(LumOnSH9 a, LumOnSH9 b)
{
    LumOnSH9 result;
    result.v0 = a.v0 + b.v0;
    result.v1 = a.v1 + b.v1;
    result.v2 = a.v2 + b.v2;
    return result;
}

/// Add two RGB SH9 structures.
LumOnSH9RGB lumonSH9RGBAdd(LumOnSH9RGB a, LumOnSH9RGB b)
{
    LumOnSH9RGB result;
    result.r = lumonSH9Add(a.r, b.r);
    result.g = lumonSH9Add(a.g, b.g);
    result.b = lumonSH9Add(a.b, b.b);
    return result;
}

/// Multiply scalar SH9 by scalar.
LumOnSH9 lumonSH9Scale(LumOnSH9 sh, float scale)
{
    LumOnSH9 result;
    result.v0 = sh.v0 * scale;
    result.v1 = sh.v1 * scale;
    result.v2 = sh.v2 * scale;
    return result;
}

/// Multiply RGB SH9 by scalar.
LumOnSH9RGB lumonSH9RGBScale(LumOnSH9RGB sh, float scale)
{
    LumOnSH9RGB result;
    result.r = lumonSH9Scale(sh.r, scale);
    result.g = lumonSH9Scale(sh.g, scale);
    result.b = lumonSH9Scale(sh.b, scale);
    return result;
}

/// Multiply scalar SH9 by RGB color (creates RGB SH9).
LumOnSH9RGB lumonSH9MulColor(LumOnSH9 sh, vec3 color)
{
    LumOnSH9RGB result;
    result.r = lumonSH9Scale(sh, color.r);
    result.g = lumonSH9Scale(sh, color.g);
    result.b = lumonSH9Scale(sh, color.b);
    return result;
}

#endif
