using VanillaGraphicsExpanded.Rendering.RHI;
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

    #region Lifetime
    /// <summary>Retires shared sampler names before their rendering context and deletion service shut down.</summary>
    public static void Dispose()
    {
        lock (Sync)
        {
            nearestClamp?.Dispose(); linearClamp?.Dispose(); shadowCompareLinearClamp?.Dispose();
            nearestClamp = null; linearClamp = null; shadowCompareLinearClamp = null;
        }
    }
    #endregion

    private static GpuSampler CreateNearestClamp()
    {
        lock (Sync)
        {
            if (nearestClamp is not null && nearestClamp.IsValid) return nearestClamp;

            nearestClamp?.Dispose();
            nearestClamp = RHICommandContext.Current.CreateSampler(new RHISamplerDescription { Label = "VGE.Sampler.NearestClamp" }).RequireResource();
            nearestClamp.SetFilter(SamplerFilter.Nearest, SamplerFilter.Nearest);
            nearestClamp.SetWrap(SamplerAddressMode.ClampToEdge, SamplerAddressMode.ClampToEdge);
            return nearestClamp;
        }
    }

    private static GpuSampler CreateLinearClamp()
    {
        lock (Sync)
        {
            if (linearClamp is not null && linearClamp.IsValid) return linearClamp;

            linearClamp?.Dispose();
            linearClamp = RHICommandContext.Current.CreateSampler(new RHISamplerDescription { Label = "VGE.Sampler.LinearClamp" }).RequireResource();
            linearClamp.SetFilter(SamplerFilter.Linear, SamplerFilter.Linear);
            linearClamp.SetWrap(SamplerAddressMode.ClampToEdge, SamplerAddressMode.ClampToEdge);
            return linearClamp;
        }
    }

    private static GpuSampler CreateShadowCompareLinearClamp()
    {
        lock (Sync)
        {
            if (shadowCompareLinearClamp is not null && shadowCompareLinearClamp.IsValid) return shadowCompareLinearClamp;

            shadowCompareLinearClamp?.Dispose();
            shadowCompareLinearClamp = RHICommandContext.Current.CreateSampler(new RHISamplerDescription { Label = "VGE.Sampler.ShadowCompareLinearClamp" }).RequireResource();
            shadowCompareLinearClamp.SetFilter(SamplerFilter.Linear, SamplerFilter.Linear);
            shadowCompareLinearClamp.SetWrap(SamplerAddressMode.ClampToEdge, SamplerAddressMode.ClampToEdge);
            shadowCompareLinearClamp.SetCompare(true, ComparisonFunction.LessOrEqual);
            return shadowCompareLinearClamp;
        }
    }
}
