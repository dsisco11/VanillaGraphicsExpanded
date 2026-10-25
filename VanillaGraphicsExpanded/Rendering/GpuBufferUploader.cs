using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Selects bound or DSA upload paths while StateCache executes native commands and publishes storage metadata.</summary>
internal static class GpuBufferUploader
{
    // 0 = unknown, 1 = enabled, -1 = disabled.
    private static int dsaEnabledState;
    private static GraphicsCapabilities? dsaCapabilities;

    #region Public API
    /// <summary>Allocates storage using explicit allocation policy and preserves the existing DSA fallback.</summary>
    public static bool BufferData(GpuBufferObject buffer, int byteCount, IntPtr data, BufferUsageHint usage)
    {
        if (TryNamedBufferData(buffer, byteCount, data, usage))
        {
            return true;
        }

        using var scope = buffer.BindScope();

        if (byteCount == 0)
        {
            StateCache.Current.BufferData(buffer, 0, IntPtr.Zero, usage);
            return false;
        }

        StateCache.Current.BufferData(buffer, byteCount, data, usage);
        return false;
    }

    /// <summary>Prefers named managed-array allocation and retains the bound marshaling fallback.</summary>
    public static void BufferData<T>(GpuBufferObject buffer, int byteCount, T[] data, BufferUsageHint usage) where T : struct
    {
        if (TryNamedBufferData(buffer, byteCount, data, usage)) return;
        using var binding = buffer.BindScope();
        StateCache.Current.BufferData(buffer, byteCount, data, usage);
    }

    /// <summary>Uploads contents using the existing DSA fallback without publishing storage metadata.</summary>
    public static bool BufferSubData(GpuBufferObject buffer, int dstOffsetBytes, int byteCount, IntPtr data)
    {
        if (TryNamedBufferSubData(buffer.BufferId, dstOffsetBytes, byteCount, data))
        {
            return true;
        }

        using var scope = buffer.BindScope();
        StateCache.Current.BufferSubData(buffer, dstOffsetBytes, byteCount, data);
        return false;
    }

    #endregion

    #region Private
    /// <summary>Attempts named allocation when supported, without treating publication failure as a DSA failure.</summary>
    private static bool TryNamedBufferData(GpuBufferObject buffer, int byteCount, IntPtr data, BufferUsageHint usage)
    {
        if (!CanUseDsa(buffer.BufferId)) return false;

        // StateCache catches only native dispatch failures; metadata publication errors propagate.
        bool executed = StateCache.Current.TryNamedBufferData(buffer, byteCount, data, usage);
        dsaEnabledState = executed ? 1 : -1;
        return executed;
    }

    /// <summary>Attempts named array allocation without retrying storage after a metadata publication failure.</summary>
    private static bool TryNamedBufferData<T>(GpuBufferObject buffer, int byteCount, T[] data, BufferUsageHint usage) where T : struct
    {
        if (!CanUseDsa(buffer.BufferId)) return false;
        bool executed = StateCache.Current.TryNamedBufferData(buffer, byteCount, data, usage);
        dsaEnabledState = executed ? 1 : -1;
        return executed;
    }

    /// <summary>Attempts the named content upload and retains the legacy native dispatch fallback.</summary>
    private static bool TryNamedBufferSubData(int bufferId, int dstOffsetBytes, int byteCount, IntPtr data)
    {
        if (!CanUseDsa(bufferId)) return false;

        try
        {
            StateCache.Current.NamedBufferSubData(bufferId, dstOffsetBytes, byteCount, data);
            dsaEnabledState = 1;
            return true;
        }
        catch
        {
            dsaEnabledState = -1;
            return false;
        }
    }

    /// <summary>Shares the cached capability and native-dispatch fallback decision across upload overloads.</summary>
    private static bool CanUseDsa(int bufferId)
    {
        if (bufferId == 0) return false;
        var capabilities = GpuSupport.Graphics;
        // Native dispatch fallback belongs to the capability snapshot that supplied the
        // creation path; another context must not inherit a prior context's DSA decision.
        if (!ReferenceEquals(dsaCapabilities, capabilities))
        {
            dsaCapabilities = capabilities;
            dsaEnabledState = capabilities.SupportsArbDirectStateAccess ? 1 : -1;
        }
        return dsaEnabledState != -1;
    }
    #endregion
}
