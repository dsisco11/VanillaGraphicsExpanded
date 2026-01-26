using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

internal static class GpuSamplers
{
    private static readonly object Sync = new();

    private static GpuSampler? nearestClamp;
    private static GpuSampler? linearClamp;
    private static GpuSampler? shadowCompareLinearClamp;

    public static GpuSampler NearestClamp => nearestClamp ??= CreateNearestClamp();

    public static GpuSampler LinearClamp => linearClamp ??= CreateLinearClamp();

    public static GpuSampler ShadowCompareLinearClamp => shadowCompareLinearClamp ??= CreateShadowCompareLinearClamp();

    private static GpuSampler CreateNearestClamp()
    {
        lock (Sync)
        {
            if (nearestClamp is not null && nearestClamp.IsValid) return nearestClamp;

            nearestClamp?.Dispose();
            nearestClamp = GpuSampler.Create("VGE.Sampler.NearestClamp");
            nearestClamp.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            nearestClamp.SetWrap(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            return nearestClamp;
        }
    }

    private static GpuSampler CreateLinearClamp()
    {
        lock (Sync)
        {
            if (linearClamp is not null && linearClamp.IsValid) return linearClamp;

            linearClamp?.Dispose();
            linearClamp = GpuSampler.Create("VGE.Sampler.LinearClamp");
            linearClamp.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            linearClamp.SetWrap(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            return linearClamp;
        }
    }

    private static GpuSampler CreateShadowCompareLinearClamp()
    {
        lock (Sync)
        {
            if (shadowCompareLinearClamp is not null && shadowCompareLinearClamp.IsValid) return shadowCompareLinearClamp;

            shadowCompareLinearClamp?.Dispose();
            shadowCompareLinearClamp = GpuSampler.Create("VGE.Sampler.ShadowCompareLinearClamp");
            shadowCompareLinearClamp.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            shadowCompareLinearClamp.SetWrap(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            shadowCompareLinearClamp.SetCompare(TextureCompareMode.CompareRefToTexture, DepthFunction.Lequal);
            return shadowCompareLinearClamp;
        }
    }
}
