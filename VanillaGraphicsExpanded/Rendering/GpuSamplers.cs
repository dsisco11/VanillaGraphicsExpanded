using VanillaGraphicsExpanded.Rendering.RHI;
using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns context-local policy samplers shared by prepared shader consumers.</summary>
internal static class GpuSamplers
{
    private static readonly object Sync = new();
    private static (nint Handle,long Generation) origin;

    /// <summary>Withdraws shared samplers when their registered context retires.</summary>
    static GpuSamplers() => Integration.RenderContextRegistry.Retiring += OnContextRetiring;

    /// <summary>Releases cached owners before the original context registration is removed.</summary>
    private static void OnContextRetiring((nint Handle,long Generation) context)
    {
        if (context == origin) Dispose();
    }

    private static GpuSampler? nearestClamp;
    private static GpuSampler? linearClamp;
    private static GpuSampler? shadowCompareLinearClamp;

    public static GpuSampler NearestClamp => CreateNearestClamp();

    public static GpuSampler LinearClamp => CreateLinearClamp();

    public static GpuSampler ShadowCompareLinearClamp => CreateShadowCompareLinearClamp();

    #region Lifetime
    /// <summary>Retires shared sampler names before their rendering context and deletion service shut down.</summary>
    public static void Dispose()
    {
        lock (Sync)
        {
            var nearest = nearestClamp;
            var linear = linearClamp;
            var shadow = shadowCompareLinearClamp;
            nearestClamp = null; linearClamp = null; shadowCompareLinearClamp = null;
            Integration.RenderingCleanup.Run(
                () => nearest?.Dispose(), () => linear?.Dispose(), () => shadow?.Dispose());
        }
    }
    #endregion

    private static GpuSampler CreateNearestClamp()
    {
        lock (Sync)
        {
            if (nearestClamp is not null && nearestClamp.IsValid) return nearestClamp;

            nearestClamp?.Dispose();
            origin = Integration.RenderContextRegistry.Current();
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
            origin = Integration.RenderContextRegistry.Current();
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
            origin = Integration.RenderContextRegistry.Current();
            shadowCompareLinearClamp = RHICommandContext.Current.CreateSampler(new RHISamplerDescription { Label = "VGE.Sampler.ShadowCompareLinearClamp" }).RequireResource();
            shadowCompareLinearClamp.SetFilter(SamplerFilter.Linear, SamplerFilter.Linear);
            shadowCompareLinearClamp.SetWrap(SamplerAddressMode.ClampToEdge, SamplerAddressMode.ClampToEdge);
            shadowCompareLinearClamp.SetCompare(true, ComparisonFunction.LessOrEqual);
            return shadowCompareLinearClamp;
        }
    }
}
