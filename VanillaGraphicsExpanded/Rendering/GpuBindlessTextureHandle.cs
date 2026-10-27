using System;
using VanillaGraphicsExpanded.Rendering.RHI;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns optional bindless residency in the context that issued the native handle.</summary>
internal sealed class GpuBindlessTextureHandle : IDisposable
{
    private readonly (nint Handle, long Generation) origin;
    private ulong handle;
    private bool resident;
    #region Public API
    /// <summary>Reports the admitted context's cached optional support.</summary>
    public static bool IsSupported => RHICommandContext.Current.Capabilities.BindlessTextures;
    /// <summary>Gets the native handle for explicit bindless integration only.</summary>
    public ulong Handle => handle;
    /// <summary>Reports residency established by this owner.</summary>
    public bool IsResident => resident;
    /// <summary>Creates a texture-only residency owner without taking texture ownership.</summary>
    public static GpuBindlessTextureHandle CreateForTexture(GpuTexture texture, bool makeResident = true)
    {
        ArgumentNullException.ThrowIfNull(texture);
        var result = new GpuBindlessTextureHandle(RHICommandContext.Current.CreateTextureHandle(texture, default));
        if (makeResident) result.MakeResident();
        return result;
    }
    /// <summary>Creates residency for a typed texture/sampler pair without accepting unqualified native names.</summary>
    public static GpuBindlessTextureHandle CreateForTextureSampler(GpuTextureRef texture, GpuSamplerRef sampler, bool makeResident = true)
    {
        if (sampler.IsEmpty) throw new ArgumentException("A sampler is required.", nameof(sampler));
        var result = new GpuBindlessTextureHandle(RHICommandContext.Current.CreateTextureHandle(texture, sampler));
        if (makeResident) result.MakeResident();
        return result;
    }
    /// <summary>Makes the original handle resident once.</summary>
    public void MakeResident()
    {
        if (handle == 0 || resident) return;
        RequireOrigin();
        RHICommandContext.Current.SetTextureHandleResidency(handle, true);
        resident = true;
    }
    /// <summary>Withdraws residency once in the issuing context.</summary>
    public void MakeNonResident()
    {
        if (handle == 0 || !resident) return;
        RequireOrigin();
        RHICommandContext.Current.SetTextureHandleResidency(handle, false);
        resident = false;
    }
    /// <summary>Releases residency without sending commands into a replacement context.</summary>
    public void Dispose()
    {
        if (GpuResource.IsCurrentResourceContext(origin)) MakeNonResident();
        resident = false;
        handle = 0;
    }
    #endregion
    #region Private
    /// <summary>Retains the issuing context independently of later context replacement.</summary>
    private GpuBindlessTextureHandle(ulong handle)
    { this.handle = handle; origin = GpuResource.CaptureCreationContext(GpuResourceKind.Texture); }
    /// <summary>Rejects use after the issuing context has retired.</summary>
    private void RequireOrigin()
    { if (!GpuResource.IsCurrentResourceContext(origin)) throw new InvalidOperationException("The bindless context has retired."); }
    #endregion
}
