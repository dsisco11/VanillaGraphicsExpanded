using System;
using System.Numerics;

using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Checks that CPU atlas directions round-trip through the shader-compatible octahedral mapping.</summary>
public sealed class WorldProbeAtlasOctahedralMappingParityTests
{
    #region Public API
    /// <summary>Verifies each texel-center direction maps back to its original atlas index.</summary>
    [Fact]
    public void TexelCenterDirections_RoundTrip_BackToSameTexelIndex()
    {
        // This validates CPU parity with the GLSL mapping used by:
        // - lumonDirectionToOctahedralUV() in lumon_octahedral.glsl
        // - ivec2(uv * LUMON_WORLDPROBE_OCTAHEDRAL_SIZE_F) in lumon_worldprobe.glsl
        // when directions are taken from texel centers.
        // Arrange
        int s = WorldProbeOctahedralSize;

        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
        // Act
                Vector3 dir = LumOnWorldProbeAtlasDirections.GetDirectionForTexelCenter(s, x, y);
                (int rx, int ry) = DirectionToOctahedralTexel_GlslParity(dir, s);
        // Assert
                Assert.Equal(x, rx);
                Assert.Equal(y, ry);
            }
        }
    }
    #endregion

    #region Private
    // Matches the default in shaders (VGE_LUMON_WORLDPROBE_OCTAHEDRAL_SIZE).
    private const int WorldProbeOctahedralSize = 16;

    /// <summary>Applies the shader-compatible UV mapping and clamps the resulting texel index.</summary>
    private static (int x, int y) DirectionToOctahedralTexel_GlslParity(Vector3 dir, int size)
    {
        Vector2 uv = DirectionToOctahedralUv_GlslParity(dir);

        int x = (int)MathF.Floor(uv.X * size);
        int y = (int)MathF.Floor(uv.Y * size);

        x = Math.Clamp(x, 0, size - 1);
        y = Math.Clamp(y, 0, size - 1);

        return (x, y);
    }

    /// <summary>Maps a direction to octahedral UV using the shader's sign and folding rules.</summary>
    private static Vector2 DirectionToOctahedralUv_GlslParity(Vector3 dir)
    {
        // Parity implementation of lumonDirectionToOctahedralUV() from lumon_octahedral.glsl.
        // Note: GLSL uses signNotZero for correct folding on axis-aligned directions.
        float ax = MathF.Abs(dir.X);
        float ay = MathF.Abs(dir.Y);
        float az = MathF.Abs(dir.Z);
        float invL1 = 1.0f / MathF.Max(1e-20f, ax + ay + az);

        float ox = dir.X * invL1;
        float oy = dir.Y * invL1;
        float oz = dir.Z * invL1;

        float u;
        float v;

        if (oz >= 0.0f)
        {
            u = ox;
            v = oy;
        }
        else
        {
            float su = ox >= 0.0f ? 1.0f : -1.0f;
            float sv = oy >= 0.0f ? 1.0f : -1.0f;

            // (1 - abs(octant.yx)) * signNotZero(octant.xy)
            u = (1.0f - MathF.Abs(oy)) * su;
            v = (1.0f - MathF.Abs(ox)) * sv;
        }

        // Map from [-1,1] to [0,1]
        return new Vector2(u * 0.5f + 0.5f, v * 0.5f + 0.5f);
    }
    #endregion
}
