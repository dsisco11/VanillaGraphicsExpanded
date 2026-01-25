using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper for an ARB_bindless_texture handle (resident/non-resident lifetime).
/// </summary>
internal sealed class GpuBindlessTextureHandle : IDisposable
{
    private ulong handle;
    private bool resident;

    /// <summary>
    /// Returns <c>true</c> when the current OpenGL context reports ARB bindless texture support.
    /// </summary>
    public static bool IsSupported => GlExtensions.Supports("GL_ARB_bindless_texture");

    /// <summary>
    /// Gets the 64-bit handle value.
    /// </summary>
    public ulong Handle => handle;

    /// <summary>
    /// Returns <c>true</c> when this wrapper currently considers the handle resident.
    /// </summary>
    public bool IsResident => resident;

    private GpuBindlessTextureHandle(ulong handle, bool resident)
    {
        this.handle = handle;
        this.resident = resident;
    }

    /// <summary>
    /// Creates a bindless handle for a texture object via <c>glGetTextureHandleARB</c>.
    /// </summary>
    public static GpuBindlessTextureHandle CreateForTexture(int textureId, bool makeResident = true)
    {
        if (!IsSupported)
        {
            throw new NotSupportedException("GL_ARB_bindless_texture is not supported by the current context.");
        }

        if (textureId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textureId), textureId, "Texture id must be > 0.");
        }

        ulong h = unchecked((ulong)GL.Arb.GetTextureHandle(textureId));
        if (h == 0)
        {
            throw new InvalidOperationException("glGetTextureHandleARB returned 0.");
        }

        if (makeResident)
        {
            GL.Arb.MakeTextureHandleResident(h);
        }

        return new GpuBindlessTextureHandle(h, resident: makeResident);
    }

    /// <summary>
    /// Creates a bindless handle for a texture + sampler pair via <c>glGetTextureSamplerHandleARB</c>.
    /// </summary>
    public static GpuBindlessTextureHandle CreateForTextureSampler(int textureId, int samplerId, bool makeResident = true)
    {
        if (!IsSupported)
        {
            throw new NotSupportedException("GL_ARB_bindless_texture is not supported by the current context.");
        }

        if (textureId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textureId), textureId, "Texture id must be > 0.");
        }

        if (samplerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(samplerId), samplerId, "Sampler id must be > 0.");
        }

        ulong h = unchecked((ulong)GL.Arb.GetTextureSamplerHandle(textureId, samplerId));
        if (h == 0)
        {
            throw new InvalidOperationException("glGetTextureSamplerHandleARB returned 0.");
        }

        if (makeResident)
        {
            GL.Arb.MakeTextureHandleResident(h);
        }

        return new GpuBindlessTextureHandle(h, resident: makeResident);
    }

    /// <summary>
    /// Makes this handle resident via <c>glMakeTextureHandleResidentARB</c>.
    /// </summary>
    public void MakeResident()
    {
        if (!IsSupported || handle == 0 || resident)
        {
            return;
        }

        GL.Arb.MakeTextureHandleResident(handle);
        resident = true;
    }

    /// <summary>
    /// Makes this handle non-resident via <c>glMakeTextureHandleNonResidentARB</c>.
    /// </summary>
    public void MakeNonResident()
    {
        if (!IsSupported || handle == 0 || !resident)
        {
            return;
        }

        GL.Arb.MakeTextureHandleNonResident(handle);
        resident = false;
    }

    /// <summary>
    /// Disposes the handle by making it non-resident (if needed).
    /// </summary>
    public void Dispose()
    {
        MakeNonResident();
        handle = 0;
    }
}

