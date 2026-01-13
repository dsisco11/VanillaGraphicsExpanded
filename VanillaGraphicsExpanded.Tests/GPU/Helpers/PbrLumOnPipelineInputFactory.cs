using System;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

internal static class PbrLumOnPipelineInputFactory
{
    public static readonly (int Width, int Height) ScreenSize = (LumOnTestInputFactory.ScreenWidth, LumOnTestInputFactory.ScreenHeight);
    public static readonly (int Width, int Height) HalfResSize = (LumOnTestInputFactory.HalfResWidth, LumOnTestInputFactory.HalfResHeight);
    public static readonly (int Width, int Height) ProbeGridSize = (LumOnTestInputFactory.ProbeGridWidth, LumOnTestInputFactory.ProbeGridHeight);
    public static readonly (int Width, int Height) AtlasSize = (LumOnTestInputFactory.OctahedralAtlasWidth, LumOnTestInputFactory.OctahedralAtlasHeight);

    public static float[] CreatePrimarySceneColorUniform(float r, float g, float b)
        => CreateRgbaUniform(ScreenSize.Width, ScreenSize.Height, r, g, b, 1f);

    public static float[] CreatePrimaryDepthUniform(float depthRaw)
        => CreateRUniform(ScreenSize.Width, ScreenSize.Height, depthRaw);

    public static float[] CreateGBufferAlbedoUniform(float r, float g, float b)
        => CreateRgbaUniform(ScreenSize.Width, ScreenSize.Height, r, g, b, 1f);

    public static float[] CreateGBufferNormalEncodedUniform(float nx, float ny, float nz)
    {
        // Encode [-1,1] normal to [0,1] to match lumonDecodeNormal() and direct lighting decode.
        float ex = nx * 0.5f + 0.5f;
        float ey = ny * 0.5f + 0.5f;
        float ez = nz * 0.5f + 0.5f;
        return CreateRgbaUniform(ScreenSize.Width, ScreenSize.Height, ex, ey, ez, 1f);
    }

    public static float[] CreateGBufferMaterialUniform(
        float roughness,
        float metallic,
        float emissiveScalar,
        float reflectivity)
    {
        // Matches pbr_direct_lighting.fsh expectations:
        // R=roughness, G=metallic, B=emissiveScalar, A=reflectivity.
        return CreateRgbaUniform(ScreenSize.Width, ScreenSize.Height,
            Clamp01(roughness),
            Clamp01(metallic),
            Math.Max(0f, emissiveScalar),
            Clamp01(reflectivity));
    }

    public static float[] CreateShadowMapUniform(float depthRaw = 1f)
        => CreateRUniform(ScreenSize.Width, ScreenSize.Height, depthRaw);

    public static float[] CreateProbeAtlasHistoryRadianceUniform(float r, float g, float b, float encodedHitDistance = 0f)
        => CreateRgbaUniform(AtlasSize.Width, AtlasSize.Height, r, g, b, encodedHitDistance);

    public static float[] CreateProbeAtlasHistoryMetaUniform(float confidence, uint flags)
    {
        float conf = Clamp01(confidence);
        float flagsBits = BitConverter.UInt32BitsToSingle(flags);
        return CreateRgUniform(AtlasSize.Width, AtlasSize.Height, conf, flagsBits);
    }

    public static float[] CreateIdentityMatrix() => LumOnTestInputFactory.CreateIdentityMatrix();

    public static float[] CreateViewMatrixIdentity() => LumOnTestInputFactory.CreateIdentityView();

    public static float[] CreateProjectionMatrix() => LumOnTestInputFactory.CreateRealisticProjection();

    public static float[] CreateInverseProjectionMatrix() => LumOnTestInputFactory.CreateRealisticInverseProjection();

    public static float[] CreateInverseViewProjMatrixForIdentityView()
    {
        // Our GPU tests run with identity view (camera at origin) for determinism.
        // In that case: inv(viewProj) == inv(projection).
        return CreateInverseProjectionMatrix();
    }

    private static float[] CreateRUniform(int width, int height, float value)
    {
        var data = new float[width * height];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = value;
        }
        return data;
    }

    private static float[] CreateRgUniform(int width, int height, float r, float g)
    {
        var data = new float[width * height * 2];
        for (int i = 0; i < width * height; i++)
        {
            int idx = i * 2;
            data[idx + 0] = r;
            data[idx + 1] = g;
        }
        return data;
    }

    private static float[] CreateRgbaUniform(int width, int height, float r, float g, float b, float a)
    {
        var data = new float[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            int idx = i * 4;
            data[idx + 0] = r;
            data[idx + 1] = g;
            data[idx + 2] = b;
            data[idx + 3] = a;
        }
        return data;
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
}
