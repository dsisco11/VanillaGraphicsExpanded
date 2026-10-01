using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR;

    /// <summary>
/// CPU-side UBO for PBR direct lighting shader parameters.
/// Layout matches VgePbrDirectLightingParamsUBO in GLSL (3552 bytes).
/// </summary>
internal sealed class PbrDirectLightingParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "VgePbrDirectLightingParamsUBO";

    private const int OffsetInvProjection = 0;          // mat4 at 0
    private const int OffsetInvModelView = 64;          // mat4 at 64
    private const int OffsetToShadowNear = 128;         // mat4 at 128
    private const int OffsetToShadowFar = 192;          // mat4 at 192
    private const int OffsetZPlanes = 256;              // vec4 at 256 (zNear, zFar, shadowRangeNear, shadowRangeFar)
    private const int OffsetShadowExtend = 272;         // vec4 at 272 (shadowZExtendNear, shadowZExtendFar, dropShadowIntensity, reserved)
    private const int OffsetLightDirection = 288;       // vec4 at 288
    private const int OffsetRgbaAmbient = 304;          // vec4 at 304
    private const int OffsetRgbaLight = 320;            // vec4 at 320
    private const int OffsetPointLightsCount = 336;     // ivec4 at 336 (count, 0, 0, 0)
    private const int OffsetPointLightsPos = 352;       // vec4[100] at 352 (1600 bytes)
    private const int OffsetPointLightsColor = 1952;    // vec4[100] at 1952 (1600 bytes)
    // Total: 3552 bytes

    /// <summary>Allocates the packed lighting parameters shared with the GLSL block.</summary>
    public PbrDirectLightingParamsUbo() : base(3552)
    {
    }

    #region Matrices

    public float[] InvProjectionMatrix
    {
        set
        {
            WriteMatrix4(OffsetInvProjection, value);
        }
    }

    public float[] InvModelViewMatrix
    {
        set
        {
            WriteMatrix4(OffsetInvModelView, value);
        }
    }

    public float[] ToShadowMapSpaceMatrixNear
    {
        set
        {
            WriteMatrix4(OffsetToShadowNear, value);
        }
    }

    public float[] ToShadowMapSpaceMatrixFar
    {
        set
        {
            WriteMatrix4(OffsetToShadowFar, value);
        }
    }

    #endregion

    #region Z Planes and Shadow Ranges

    /// <summary>
    /// Camera Z planes and shadow ranges.
    /// </summary>
    public (float zNear, float zFar, float shadowRangeNear, float shadowRangeFar) ZPlanesAndShadowRanges
    {
        set
        {
            WriteVector4(OffsetZPlanes, new(value.zNear, value.zFar, value.shadowRangeNear, value.shadowRangeFar));
        }
    }

    /// <summary>
    /// Shadow Z extends and drop shadow intensity.
    /// </summary>
    public (float shadowZExtendNear, float shadowZExtendFar, float dropShadowIntensity) ShadowExtendAndDrop
    {
        set
        {
            WriteVector4(OffsetShadowExtend, new(value.shadowZExtendNear, value.shadowZExtendFar, value.dropShadowIntensity, 0f));
        }
    }

    #endregion

    #region Lighting

    public Vector3 LightDirection
    {
        set
        {
            WriteVector4(OffsetLightDirection, new(value.X, value.Y, value.Z, 0f));
        }
    }

    public Vector3 RgbaAmbientIn
    {
        set
        {
            WriteVector4(OffsetRgbaAmbient, new(value.X, value.Y, value.Z, 0f));
        }
    }

    /// <summary>Sets physical solar irradiance; the padding component is always zero.</summary>
    public Vector3 RgbaLightIn
    {
        set
        {
            WriteVector4(OffsetRgbaLight, new(value.X, value.Y, value.Z, 0f));
        }
    }

    /// <summary>
    /// Sets point lights count and arrays.
    /// </summary>
    public void SetPointLights(int count, float[]? positions3, float[]? colors3)
    {
        int clampedCount = System.Math.Clamp(count, 0, 100);

        WriteIntVector4(OffsetPointLightsCount, clampedCount, 0, 0, 0);

        // Positions (vec4[100])
        if (positions3 is not null)
        {
            int maxVec3 = System.Math.Min(clampedCount, positions3.Length / 3);
            for (int i = 0; i < maxVec3; i++)
            {
                int src = i * 3;
                int dst = OffsetPointLightsPos + (i * 16);
                WriteVector4(dst, new(positions3[src + 0], positions3[src + 1], positions3[src + 2], 0f));
            }

            for (int i = maxVec3; i < clampedCount; i++)
            {
                WriteVector4(OffsetPointLightsPos + (i * 16), new(0f, 0f, 0f, 0f));
            }
        }
        else
        {
            for (int i = 0; i < clampedCount; i++)
            {
                WriteVector4(OffsetPointLightsPos + (i * 16), new(0f, 0f, 0f, 0f));
            }
        }

        // Colors (vec4[100])
        if (colors3 is not null)
        {
            int maxVec3 = System.Math.Min(clampedCount, colors3.Length / 3);
            for (int i = 0; i < maxVec3; i++)
            {
                int src = i * 3;
                int dst = OffsetPointLightsColor + (i * 16);
                WriteVector4(dst, new(colors3[src + 0], colors3[src + 1], colors3[src + 2], 0f));
            }

            for (int i = maxVec3; i < clampedCount; i++)
            {
                WriteVector4(OffsetPointLightsColor + (i * 16), new(0f, 0f, 0f, 0f));
            }
        }
        else
        {
            for (int i = 0; i < clampedCount; i++)
            {
                WriteVector4(OffsetPointLightsColor + (i * 16), new(0f, 0f, 0f, 0f));
            }
        }

    }

    #endregion
}
