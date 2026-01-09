using System;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>
/// Factory for creating deterministic test input data for LumOn shader functional tests.
/// All generators produce float arrays suitable for upload via DynamicTexture.UploadData().
/// </summary>
/// <remarks>
/// Test configuration:
/// - Screen buffer: 4×4 pixels
/// - Half-res buffer: 2×2 pixels  
/// - Probe grid: 2×2 probes
/// - Octahedral atlas: 16×16 texels (8 per probe)
/// - All RGBA formats use 4 channels per pixel
/// </remarks>
public static class LumOnTestInputFactory
{
    #region Constants

    /// <summary>Screen buffer width for tests.</summary>
    public const int ScreenWidth = 4;

    /// <summary>Screen buffer height for tests.</summary>
    public const int ScreenHeight = 4;

    /// <summary>Half-resolution buffer width.</summary>
    public const int HalfResWidth = 2;

    /// <summary>Half-resolution buffer height.</summary>
    public const int HalfResHeight = 2;

    /// <summary>Probe grid width (number of probes).</summary>
    public const int ProbeGridWidth = 2;

    /// <summary>Probe grid height (number of probes).</summary>
    public const int ProbeGridHeight = 2;

    /// <summary>Texels per probe in octahedral atlas (8×8 per probe).</summary>
    public const int OctahedralTexelsPerProbe = 8;

    /// <summary>Octahedral atlas width (ProbeGridWidth × OctahedralTexelsPerProbe).</summary>
    public const int OctahedralAtlasWidth = ProbeGridWidth * OctahedralTexelsPerProbe; // 16

    /// <summary>Octahedral atlas height (ProbeGridHeight × OctahedralTexelsPerProbe).</summary>
    public const int OctahedralAtlasHeight = ProbeGridHeight * OctahedralTexelsPerProbe; // 16

    /// <summary>Default near clip plane for test matrices.</summary>
    public const float DefaultZNear = 0.1f;

    /// <summary>Default far clip plane for test matrices.</summary>
    public const float DefaultZFar = 100f;

    #endregion

    #region Depth Buffer Generators (4×4, single channel R32F or RGBA16F)

    /// <summary>
    /// Creates a 4×4 depth buffer with linear gradient from 0.1 to 0.9.
    /// Top-left = 0.1, bottom-right = 0.9.
    /// </summary>
    /// <param name="channels">Number of channels (1 for R32F, 4 for RGBA16F).</param>
    /// <returns>Float array for texture upload.</returns>
    public static float[] CreateDepthBufferLinearRamp(int channels = 1)
    {
        var data = new float[ScreenWidth * ScreenHeight * channels];

        for (int y = 0; y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                // Normalized position (0-1)
                float u = x / (float)(ScreenWidth - 1);
                float v = y / (float)(ScreenHeight - 1);

                // Linear ramp from 0.1 to 0.9
                float depth = 0.1f + 0.8f * (u + v) / 2f;

                int idx = (y * ScreenWidth + x) * channels;
                data[idx] = depth;

                // Fill remaining channels with 0 if RGBA
                for (int c = 1; c < channels; c++)
                    data[idx + c] = 0f;
            }
        }

        return data;
    }

    /// <summary>
    /// Creates a 4×4 depth buffer with uniform depth value.
    /// </summary>
    /// <param name="depth">Depth value (0-1, where 1 = far/sky).</param>
    /// <param name="channels">Number of channels (1 for R32F, 4 for RGBA16F).</param>
    /// <returns>Float array for texture upload.</returns>
    public static float[] CreateDepthBufferUniform(float depth, int channels = 1)
    {
        var data = new float[ScreenWidth * ScreenHeight * channels];

        for (int i = 0; i < ScreenWidth * ScreenHeight; i++)
        {
            int idx = i * channels;
            data[idx] = depth;

            for (int c = 1; c < channels; c++)
                data[idx + c] = 0f;
        }

        return data;
    }

    /// <summary>
    /// Creates a 4×4 depth buffer with checkerboard pattern.
    /// Alternates between near (0.2) and far (0.8) depth values.
    /// </summary>
    /// <param name="channels">Number of channels (1 for R32F, 4 for RGBA16F).</param>
    /// <returns>Float array for texture upload.</returns>
    public static float[] CreateDepthBufferCheckerboard(int channels = 1)
    {
        const float nearDepth = 0.2f;
        const float farDepth = 0.8f;

        var data = new float[ScreenWidth * ScreenHeight * channels];

        for (int y = 0; y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                bool isNear = (x + y) % 2 == 0;
                float depth = isNear ? nearDepth : farDepth;

                int idx = (y * ScreenWidth + x) * channels;
                data[idx] = depth;

                for (int c = 1; c < channels; c++)
                    data[idx + c] = 0f;
            }
        }

        return data;
    }

    #endregion

    #region Normal Buffer Generators (4×4, RGBA16F)

    /// <summary>
    /// Creates a 4×4 normal buffer with all normals pointing upward (0, 1, 0).
    /// Format: RGBA16F where RGB = normal.xyz, A = 1.0.
    /// </summary>
    /// <returns>Float array for texture upload (64 floats).</returns>
    public static float[] CreateNormalBufferUpward()
    {
        var data = new float[ScreenWidth * ScreenHeight * 4];

        for (int i = 0; i < ScreenWidth * ScreenHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = 0f;  // X
            data[idx + 1] = 1f;  // Y (up)
            data[idx + 2] = 0f;  // Z
            data[idx + 3] = 1f;  // A (valid)
        }

        return data;
    }

    /// <summary>
    /// Creates a 4×4 normal buffer with one axis-aligned direction per quadrant.
    /// Top-left: +X (1,0,0), Top-right: +Y (0,1,0)
    /// Bottom-left: +Z (0,0,1), Bottom-right: -Y (0,-1,0)
    /// </summary>
    /// <returns>Float array for texture upload (64 floats).</returns>
    public static float[] CreateNormalBufferAxisAligned()
    {
        var data = new float[ScreenWidth * ScreenHeight * 4];

        // Define normals for each quadrant
        (float x, float y, float z)[] quadrantNormals =
        [
            (1f, 0f, 0f),   // Top-left: +X
            (0f, 1f, 0f),   // Top-right: +Y
            (0f, 0f, 1f),   // Bottom-left: +Z
            (0f, -1f, 0f)   // Bottom-right: -Y
        ];

        for (int py = 0; py < ScreenHeight; py++)
        {
            for (int px = 0; px < ScreenWidth; px++)
            {
                // Determine quadrant (0-3)
                int qx = px < ScreenWidth / 2 ? 0 : 1;
                int qy = py < ScreenHeight / 2 ? 0 : 1;
                int quadrant = qy * 2 + qx;

                var (nx, ny, nz) = quadrantNormals[quadrant];

                int idx = (py * ScreenWidth + px) * 4;
                data[idx + 0] = nx;
                data[idx + 1] = ny;
                data[idx + 2] = nz;
                data[idx + 3] = 1f; // Valid
            }
        }

        return data;
    }

    /// <summary>
    /// Creates a 4×4 normal buffer with normals derived from depth discontinuities.
    /// Simple gradient-based normal estimation for testing edge detection.
    /// </summary>
    /// <param name="depthBuffer">Depth buffer to derive normals from (single channel).</param>
    /// <returns>Float array for texture upload (64 floats).</returns>
    public static float[] CreateNormalBufferFromDepth(float[] depthBuffer)
    {
        var data = new float[ScreenWidth * ScreenHeight * 4];

        for (int y = 0; y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                // Sample neighboring depths
                float center = GetDepth(depthBuffer, x, y);
                float left = GetDepth(depthBuffer, x - 1, y);
                float right = GetDepth(depthBuffer, x + 1, y);
                float up = GetDepth(depthBuffer, x, y - 1);
                float down = GetDepth(depthBuffer, x, y + 1);

                // Compute gradient
                float dx = (right - left) * 0.5f;
                float dy = (down - up) * 0.5f;

                // Construct normal from gradient (simplified)
                float nx = -dx;
                float ny = 1f;
                float nz = -dy;

                // Normalize
                float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len > 0.0001f)
                {
                    nx /= len;
                    ny /= len;
                    nz /= len;
                }
                else
                {
                    nx = 0f;
                    ny = 1f;
                    nz = 0f;
                }

                int idx = (y * ScreenWidth + x) * 4;
                data[idx + 0] = nx;
                data[idx + 1] = ny;
                data[idx + 2] = nz;
                data[idx + 3] = 1f; // Valid
            }
        }

        return data;

        static float GetDepth(float[] buffer, int x, int y)
        {
            x = Math.Clamp(x, 0, ScreenWidth - 1);
            y = Math.Clamp(y, 0, ScreenHeight - 1);
            return buffer[y * ScreenWidth + x];
        }
    }

    #endregion

    #region Probe Anchor Generators (2×2, RGBA16F)

    /// <summary>
    /// Creates a 2×2 probe position buffer with probes at known world coordinates.
    /// Probes form a grid in the XZ plane at Y=0.
    /// Format: RGBA16F where RGB = position.xyz, A = validity (1.0 = valid).
    /// </summary>
    /// <param name="spacing">World-space distance between probes.</param>
    /// <returns>Float array for texture upload (16 floats).</returns>
    public static float[] CreateProbePositions(float spacing = 2f)
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];

        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                // World position: grid in XZ plane
                float worldX = (px - 0.5f) * spacing;
                float worldY = 0f;
                float worldZ = (py - 0.5f) * spacing;

                int idx = (py * ProbeGridWidth + px) * 4;
                data[idx + 0] = worldX;
                data[idx + 1] = worldY;
                data[idx + 2] = worldZ;
                data[idx + 3] = 1f; // Valid
            }
        }

        return data;
    }

    /// <summary>
    /// Creates a 2×2 probe normal buffer with all normals pointing upward.
    /// Format: RGBA16F where RGB = normal.xyz, A = reserved.
    /// </summary>
    /// <returns>Float array for texture upload (16 floats).</returns>
    public static float[] CreateProbeNormals()
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];

        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = 0f;  // X
            data[idx + 1] = 1f;  // Y (up)
            data[idx + 2] = 0f;  // Z
            data[idx + 3] = 0f;  // Reserved
        }

        return data;
    }

    /// <summary>
    /// Creates a 2×2 probe validity buffer with all probes marked as valid.
    /// For use with shaders that check validity in a separate texture.
    /// </summary>
    /// <returns>Float array for texture upload (16 floats for RGBA).</returns>
    public static float[] CreateProbeValidity()
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];

        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = 1f;  // Validity flag
            data[idx + 1] = 0f;
            data[idx + 2] = 0f;
            data[idx + 3] = 1f;
        }

        return data;
    }

    /// <summary>
    /// Creates a 2×2 probe position buffer with specific validity per probe.
    /// </summary>
    /// <param name="validities">Array of 4 booleans indicating validity for each probe.</param>
    /// <param name="spacing">World-space distance between probes.</param>
    /// <returns>Float array for texture upload (16 floats).</returns>
    public static float[] CreateProbePositionsWithValidity(bool[] validities, float spacing = 2f)
    {
        if (validities.Length != ProbeGridWidth * ProbeGridHeight)
            throw new ArgumentException($"Expected {ProbeGridWidth * ProbeGridHeight} validity values", nameof(validities));

        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];

        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int probeIdx = py * ProbeGridWidth + px;
                float worldX = (px - 0.5f) * spacing;
                float worldY = 0f;
                float worldZ = (py - 0.5f) * spacing;

                int idx = probeIdx * 4;
                data[idx + 0] = worldX;
                data[idx + 1] = worldY;
                data[idx + 2] = worldZ;
                data[idx + 3] = validities[probeIdx] ? 1f : 0f;
            }
        }

        return data;
    }

    #endregion

    #region Radiance Generators (Octahedral Atlas 16×16, SH Coefficients 2×2)

    /// <summary>
    /// Creates a 16×16 octahedral radiance atlas with solid colors per quadrant.
    /// Each probe's 8×8 region gets a distinct color: Red, Green, Blue, White.
    /// Format: RGBA16F where RGB = radiance, A = hit distance (encoded).
    /// </summary>
    /// <returns>Float array for texture upload (1024 floats).</returns>
    public static float[] CreateRadianceAtlasSolidQuadrants()
    {
        var data = new float[OctahedralAtlasWidth * OctahedralAtlasHeight * 4];

        // Colors for each probe quadrant (RGBW)
        (float r, float g, float b)[] probeColors =
        [
            (1f, 0f, 0f),  // Probe 0,0: Red
            (0f, 1f, 0f),  // Probe 1,0: Green
            (0f, 0f, 1f),  // Probe 0,1: Blue
            (1f, 1f, 1f)   // Probe 1,1: White
        ];

        for (int y = 0; y < OctahedralAtlasHeight; y++)
        {
            for (int x = 0; x < OctahedralAtlasWidth; x++)
            {
                // Determine which probe this texel belongs to
                int probeX = x / OctahedralTexelsPerProbe;
                int probeY = y / OctahedralTexelsPerProbe;
                int probeIdx = probeY * ProbeGridWidth + probeX;

                var (r, g, b) = probeColors[probeIdx];

                int idx = (y * OctahedralAtlasWidth + x) * 4;
                data[idx + 0] = r;
                data[idx + 1] = g;
                data[idx + 2] = b;
                data[idx + 3] = 1f; // Hit distance (1.0 = near hit)
            }
        }

        return data;
    }

    /// <summary>
    /// Creates a 16×16 octahedral radiance atlas with horizontal gradient.
    /// Left edge = black (0,0,0), right edge = white (1,1,1).
    /// </summary>
    /// <returns>Float array for texture upload (1024 floats).</returns>
    public static float[] CreateRadianceAtlasGradient()
    {
        var data = new float[OctahedralAtlasWidth * OctahedralAtlasHeight * 4];

        for (int y = 0; y < OctahedralAtlasHeight; y++)
        {
            for (int x = 0; x < OctahedralAtlasWidth; x++)
            {
                float t = x / (float)(OctahedralAtlasWidth - 1);

                int idx = (y * OctahedralAtlasWidth + x) * 4;
                data[idx + 0] = t;
                data[idx + 1] = t;
                data[idx + 2] = t;
                data[idx + 3] = 1f;
            }
        }

        return data;
    }

    /// <summary>
    /// Creates a 2×2 SH coefficients buffer for uniform solid color.
    /// Uses L1 spherical harmonics (4 coefficients per color channel).
    /// For a uniform color, only the DC term (L0) is non-zero.
    /// </summary>
    /// <param name="r">Red component (0-1).</param>
    /// <param name="g">Green component (0-1).</param>
    /// <param name="b">Blue component (0-1).</param>
    /// <returns>Float array for texture upload.</returns>
    /// <remarks>
    /// SH L1 basis functions:
    /// Y0 = 0.282095 (DC term)
    /// Y1 = 0.488603 * y
    /// Y2 = 0.488603 * z  
    /// Y3 = 0.488603 * x
    /// For uniform irradiance, coefficients = color * sqrt(4*PI) / Y0
    /// </remarks>
    public static float[] CreateSHCoefficientsUniform(float r, float g, float b)
    {
        // For uniform radiance, we need 2 textures (RGBA16F each) per probe for L1 SH
        // Texture 0: (R_L0, R_L1x, R_L1y, R_L1z)
        // Texture 1: (G_L0, G_L1x, G_L1y, G_L1z), (B_L0, B_L1x, B_L1y, B_L1z) packed

        // For simplicity, return a single RGBA texture with the DC term
        // The uniform color's SH representation: coefficient = color * sqrt(PI)
        const float shScale = 1.7724539f; // sqrt(PI)

        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];

        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            // Store DC coefficients (L0) scaled for SH
            data[idx + 0] = r * shScale;
            data[idx + 1] = g * shScale;
            data[idx + 2] = b * shScale;
            data[idx + 3] = 1f; // Valid
        }

        return data;
    }

    #endregion

    #region Matrix Generators (4×4 = 16 floats, column-major)

    /// <summary>
    /// Creates a 4×4 identity matrix in column-major order.
    /// </summary>
    /// <returns>16-element float array.</returns>
    public static float[] CreateIdentityMatrix()
    {
        return
        [
            1f, 0f, 0f, 0f,  // Column 0
            0f, 1f, 0f, 0f,  // Column 1
            0f, 0f, 1f, 0f,  // Column 2
            0f, 0f, 0f, 1f   // Column 3
        ];
    }

    /// <summary>
    /// Creates an identity view matrix (camera at origin, looking down -Z).
    /// </summary>
    /// <returns>16-element float array in column-major order.</returns>
    public static float[] CreateIdentityView() => CreateIdentityMatrix();

    /// <summary>
    /// Creates an identity projection matrix.
    /// </summary>
    /// <returns>16-element float array in column-major order.</returns>
    public static float[] CreateIdentityProjection() => CreateIdentityMatrix();

    /// <summary>
    /// Creates a realistic perspective projection matrix using default test parameters.
    /// Use this instead of identity projection for proper depth reconstruction.
    /// </summary>
    /// <returns>Perspective projection matrix in column-major order.</returns>
    public static float[] CreateRealisticProjection()
    {
        return CreateSimplePerspective(
            MathF.PI / 3f,  // 60° vertical FOV
            1.0f,           // Square aspect (4×4 screen)
            DefaultZNear,   // 0.1
            DefaultZFar     // 100
        );
    }

    /// <summary>
    /// Creates an inverse perspective projection matrix using default test parameters.
    /// Use this for shaders that need to reconstruct view-space positions from depth.
    /// </summary>
    /// <returns>Inverse perspective projection matrix in column-major order.</returns>
    public static float[] CreateRealisticInverseProjection()
    {
        var projection = CreateRealisticProjection();
        return CreateInverseMatrix(projection);
    }

    /// <summary>
    /// Creates a simple perspective projection matrix.
    /// </summary>
    /// <param name="fovRadians">Vertical field of view in radians.</param>
    /// <param name="aspect">Aspect ratio (width/height).</param>
    /// <param name="zNear">Near clip plane distance.</param>
    /// <param name="zFar">Far clip plane distance.</param>
    /// <returns>16-element float array in column-major order.</returns>
    public static float[] CreateSimplePerspective(float fovRadians, float aspect, float zNear, float zFar)
    {
        float tanHalfFov = MathF.Tan(fovRadians / 2f);
        float f = 1f / tanHalfFov;

        // OpenGL-style perspective matrix (column-major)
        return
        [
            f / aspect, 0f, 0f, 0f,                                          // Column 0
            0f, f, 0f, 0f,                                                   // Column 1
            0f, 0f, (zFar + zNear) / (zNear - zFar), -1f,                   // Column 2
            0f, 0f, (2f * zFar * zNear) / (zNear - zFar), 0f                // Column 3
        ];
    }

    /// <summary>
    /// Creates a simple orthographic projection matrix.
    /// </summary>
    /// <param name="left">Left clipping plane.</param>
    /// <param name="right">Right clipping plane.</param>
    /// <param name="bottom">Bottom clipping plane.</param>
    /// <param name="top">Top clipping plane.</param>
    /// <param name="zNear">Near clip plane.</param>
    /// <param name="zFar">Far clip plane.</param>
    /// <returns>16-element float array in column-major order.</returns>
    public static float[] CreateOrthographicProjection(float left, float right, float bottom, float top, float zNear, float zFar)
    {
        float width = right - left;
        float height = top - bottom;
        float depth = zFar - zNear;

        return
        [
            2f / width, 0f, 0f, 0f,                                      // Column 0
            0f, 2f / height, 0f, 0f,                                     // Column 1
            0f, 0f, -2f / depth, 0f,                                     // Column 2
            -(right + left) / width, -(top + bottom) / height, -(zFar + zNear) / depth, 1f  // Column 3
        ];
    }

    /// <summary>
    /// Creates a translation matrix.
    /// </summary>
    /// <param name="x">X translation.</param>
    /// <param name="y">Y translation.</param>
    /// <param name="z">Z translation.</param>
    /// <returns>16-element float array in column-major order.</returns>
    public static float[] CreateTranslationMatrix(float x, float y, float z)
    {
        return
        [
            1f, 0f, 0f, 0f,  // Column 0
            0f, 1f, 0f, 0f,  // Column 1
            0f, 0f, 1f, 0f,  // Column 2
            x, y, z, 1f      // Column 3
        ];
    }

    /// <summary>
    /// Computes the inverse of a 4×4 matrix.
    /// </summary>
    /// <param name="m">16-element matrix in column-major order.</param>
    /// <returns>16-element inverse matrix, or identity if singular.</returns>
    public static float[] CreateInverseMatrix(float[] m)
    {
        if (m.Length != 16)
            throw new ArgumentException("Matrix must have 16 elements", nameof(m));

        // Using Cramer's rule for 4x4 matrix inversion
        // This is the standard implementation

        float[] inv = new float[16];

        inv[0] = m[5] * m[10] * m[15] - m[5] * m[11] * m[14] - m[9] * m[6] * m[15] +
                 m[9] * m[7] * m[14] + m[13] * m[6] * m[11] - m[13] * m[7] * m[10];

        inv[4] = -m[4] * m[10] * m[15] + m[4] * m[11] * m[14] + m[8] * m[6] * m[15] -
                  m[8] * m[7] * m[14] - m[12] * m[6] * m[11] + m[12] * m[7] * m[10];

        inv[8] = m[4] * m[9] * m[15] - m[4] * m[11] * m[13] - m[8] * m[5] * m[15] +
                 m[8] * m[7] * m[13] + m[12] * m[5] * m[11] - m[12] * m[7] * m[9];

        inv[12] = -m[4] * m[9] * m[14] + m[4] * m[10] * m[13] + m[8] * m[5] * m[14] -
                   m[8] * m[6] * m[13] - m[12] * m[5] * m[10] + m[12] * m[6] * m[9];

        inv[1] = -m[1] * m[10] * m[15] + m[1] * m[11] * m[14] + m[9] * m[2] * m[15] -
                  m[9] * m[3] * m[14] - m[13] * m[2] * m[11] + m[13] * m[3] * m[10];

        inv[5] = m[0] * m[10] * m[15] - m[0] * m[11] * m[14] - m[8] * m[2] * m[15] +
                 m[8] * m[3] * m[14] + m[12] * m[2] * m[11] - m[12] * m[3] * m[10];

        inv[9] = -m[0] * m[9] * m[15] + m[0] * m[11] * m[13] + m[8] * m[1] * m[15] -
                  m[8] * m[3] * m[13] - m[12] * m[1] * m[11] + m[12] * m[3] * m[9];

        inv[13] = m[0] * m[9] * m[14] - m[0] * m[10] * m[13] - m[8] * m[1] * m[14] +
                  m[8] * m[2] * m[13] + m[12] * m[1] * m[10] - m[12] * m[2] * m[9];

        inv[2] = m[1] * m[6] * m[15] - m[1] * m[7] * m[14] - m[5] * m[2] * m[15] +
                 m[5] * m[3] * m[14] + m[13] * m[2] * m[7] - m[13] * m[3] * m[6];

        inv[6] = -m[0] * m[6] * m[15] + m[0] * m[7] * m[14] + m[4] * m[2] * m[15] -
                  m[4] * m[3] * m[14] - m[12] * m[2] * m[7] + m[12] * m[3] * m[6];

        inv[10] = m[0] * m[5] * m[15] - m[0] * m[7] * m[13] - m[4] * m[1] * m[15] +
                  m[4] * m[3] * m[13] + m[12] * m[1] * m[7] - m[12] * m[3] * m[5];

        inv[14] = -m[0] * m[5] * m[14] + m[0] * m[6] * m[13] + m[4] * m[1] * m[14] -
                   m[4] * m[2] * m[13] - m[12] * m[1] * m[6] + m[12] * m[2] * m[5];

        inv[3] = -m[1] * m[6] * m[11] + m[1] * m[7] * m[10] + m[5] * m[2] * m[11] -
                  m[5] * m[3] * m[10] - m[9] * m[2] * m[7] + m[9] * m[3] * m[6];

        inv[7] = m[0] * m[6] * m[11] - m[0] * m[7] * m[10] - m[4] * m[2] * m[11] +
                 m[4] * m[3] * m[10] + m[8] * m[2] * m[7] - m[8] * m[3] * m[6];

        inv[11] = -m[0] * m[5] * m[11] + m[0] * m[7] * m[9] + m[4] * m[1] * m[11] -
                   m[4] * m[3] * m[9] - m[8] * m[1] * m[7] + m[8] * m[3] * m[5];

        inv[15] = m[0] * m[5] * m[10] - m[0] * m[6] * m[9] - m[4] * m[1] * m[10] +
                  m[4] * m[2] * m[9] + m[8] * m[1] * m[6] - m[8] * m[2] * m[5];

        float det = m[0] * inv[0] + m[1] * inv[4] + m[2] * inv[8] + m[3] * inv[12];

        if (MathF.Abs(det) < 1e-10f)
        {
            // Matrix is singular, return identity
            return CreateIdentityMatrix();
        }

        det = 1f / det;

        for (int i = 0; i < 16; i++)
            inv[i] *= det;

        return inv;
    }

    #endregion

    #region Scene Input Generators (4×4, RGBA16F)

    /// <summary>
    /// Creates a 4×4 albedo buffer with uniform color.
    /// Format: RGBA16F where RGB = albedo color, A = 1.0.
    /// </summary>
    /// <param name="r">Red component (0-1).</param>
    /// <param name="g">Green component (0-1).</param>
    /// <param name="b">Blue component (0-1).</param>
    /// <returns>Float array for texture upload (64 floats).</returns>
    public static float[] CreateAlbedoBuffer(float r, float g, float b)
    {
        var data = new float[ScreenWidth * ScreenHeight * 4];

        for (int i = 0; i < ScreenWidth * ScreenHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = r;
            data[idx + 1] = g;
            data[idx + 2] = b;
            data[idx + 3] = 1f;
        }

        return data;
    }

    /// <summary>
    /// Creates a 4×4 direct lighting buffer with a simple lit scene.
    /// Gradient from dark (bottom) to bright (top) simulating directional light.
    /// </summary>
    /// <param name="baseColor">Base light color.</param>
    /// <returns>Float array for texture upload (64 floats).</returns>
    public static float[] CreateDirectLightingBuffer((float r, float g, float b) baseColor)
    {
        var data = new float[ScreenWidth * ScreenHeight * 4];

        for (int y = 0; y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                // Brightness increases from bottom to top
                float brightness = (ScreenHeight - 1 - y) / (float)(ScreenHeight - 1);
                brightness = 0.2f + brightness * 0.8f; // Range 0.2 to 1.0

                int idx = (y * ScreenWidth + x) * 4;
                data[idx + 0] = baseColor.r * brightness;
                data[idx + 1] = baseColor.g * brightness;
                data[idx + 2] = baseColor.b * brightness;
                data[idx + 3] = 1f;
            }
        }

        return data;
    }

    /// <summary>
    /// Creates a 4×4 direct lighting buffer with uniform color.
    /// </summary>
    /// <param name="r">Red component.</param>
    /// <param name="g">Green component.</param>
    /// <param name="b">Blue component.</param>
    /// <returns>Float array for texture upload (64 floats).</returns>
    public static float[] CreateDirectLightingBufferUniform(float r, float g, float b)
    {
        return CreateAlbedoBuffer(r, g, b); // Same format
    }

    /// <summary>
    /// Creates a 4×4 material buffer with uniform metallic/roughness.
    /// Format: RGBA16F where R = metallic, G = roughness, BA = reserved.
    /// </summary>
    /// <param name="metallic">Metallic value (0 = dielectric, 1 = metal).</param>
    /// <param name="roughness">Roughness value (0 = smooth, 1 = rough).</param>
    /// <returns>Float array for texture upload (64 floats).</returns>
    public static float[] CreateMaterialBuffer(float metallic, float roughness)
    {
        var data = new float[ScreenWidth * ScreenHeight * 4];

        for (int i = 0; i < ScreenWidth * ScreenHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = metallic;
            data[idx + 1] = roughness;
            data[idx + 2] = 0f;  // Reserved
            data[idx + 3] = 1f;  // Reserved/valid
        }

        return data;
    }

    #endregion

    #region Half-Resolution Generators (2×2)

    /// <summary>
    /// Creates a 2×2 half-resolution indirect buffer with uniform color.
    /// </summary>
    /// <param name="r">Red component.</param>
    /// <param name="g">Green component.</param>
    /// <param name="b">Blue component.</param>
    /// <returns>Float array for texture upload (16 floats).</returns>
    public static float[] CreateHalfResIndirectUniform(float r, float g, float b)
    {
        var data = new float[HalfResWidth * HalfResHeight * 4];

        for (int i = 0; i < HalfResWidth * HalfResHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = r;
            data[idx + 1] = g;
            data[idx + 2] = b;
            data[idx + 3] = 1f;
        }

        return data;
    }

    /// <summary>
    /// Creates a 2×2 half-resolution indirect buffer with gradient.
    /// Top-left = black, bottom-right = white.
    /// </summary>
    /// <returns>Float array for texture upload (16 floats).</returns>
    public static float[] CreateHalfResIndirectGradient()
    {
        var data = new float[HalfResWidth * HalfResHeight * 4];

        for (int y = 0; y < HalfResHeight; y++)
        {
            for (int x = 0; x < HalfResWidth; x++)
            {
                float t = (x + y) / (float)(HalfResWidth + HalfResHeight - 2);

                int idx = (y * HalfResWidth + x) * 4;
                data[idx + 0] = t;
                data[idx + 1] = t;
                data[idx + 2] = t;
                data[idx + 3] = 1f;
            }
        }

        return data;
    }

    #endregion

    #region Temporal History Generators

    /// <summary>
    /// Creates a temporal history meta buffer (2×2 for probes).
    /// Format: RGBA16F where R = depth, G = normal.x, B = normal.y, A = accumCount.
    /// </summary>
    /// <param name="depth">Linear depth value.</param>
    /// <param name="accumCount">Accumulation count (frames accumulated).</param>
    /// <returns>Float array for texture upload (16 floats).</returns>
    public static float[] CreateTemporalMetaBuffer(float depth, float accumCount)
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];

        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = depth;
            data[idx + 1] = 0f;        // Normal.x (0 = upward projected)
            data[idx + 2] = 1f;        // Normal.y (pointing up)
            data[idx + 3] = accumCount;
        }

        return data;
    }

    #endregion
}
