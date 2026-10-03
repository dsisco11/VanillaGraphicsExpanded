using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Stores optional per-draw-buffer blend settings.</summary>
public sealed partial class GpuFramebuffer
{
    #region Public API

    /// <summary>
    /// Sets whether blending is enabled for a specific color attachment index (i.e. draw buffer index).
    /// This config is only applied when <see cref="ApplyAttachmentBlendState"/> is called.
    /// </summary>
    /// <remarks>
    /// OpenGL state is global; callers should ensure they apply a complete blend state set per-pass.
    /// </remarks>
    public void SetAttachmentBlendEnabled(int attachmentIndex, bool enabled)
    {
        if (attachmentIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attachmentIndex), attachmentIndex, "Attachment index must be >= 0.");
        }

        EnsureAttachmentBlendCapacity(attachmentIndex);
        attachmentBlendEnabled![attachmentIndex] = enabled;
    }

    /// <summary>
    /// Sets the blend function for a specific color attachment index (i.e. draw buffer index).
    /// This config is only applied when <see cref="ApplyAttachmentBlendState"/> is called.
    /// </summary>
    public void SetAttachmentBlendFunc(
        int attachmentIndex,
        BlendingFactorSrc srcRgb,
        BlendingFactorDest dstRgb,
        BlendingFactorSrc srcAlpha,
        BlendingFactorDest dstAlpha)
    {
        if (attachmentIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attachmentIndex), attachmentIndex, "Attachment index must be >= 0.");
        }

        EnsureAttachmentBlendCapacity(attachmentIndex);
        attachmentBlendFunc![attachmentIndex] = new GlBlendFunc(srcRgb, dstRgb, srcAlpha, dstAlpha);
    }

    /// <summary>
    /// Convenience helper for setting both enable and blend function for a specific attachment.
    /// </summary>
    public void SetAttachmentBlendState(
        int attachmentIndex,
        bool enabled,
        BlendingFactorSrc srcRgb,
        BlendingFactorDest dstRgb,
        BlendingFactorSrc srcAlpha,
        BlendingFactorDest dstAlpha)
    {
        SetAttachmentBlendEnabled(attachmentIndex, enabled);
        SetAttachmentBlendFunc(attachmentIndex, srcRgb, dstRgb, srcAlpha, dstAlpha);
    }

    /// <summary>
    /// Clears any stored per-attachment blend settings for the given attachment index.
    /// </summary>
    public void ClearAttachmentBlendState(int attachmentIndex)
    {
        if (attachmentIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attachmentIndex), attachmentIndex, "Attachment index must be >= 0.");
        }

        if (attachmentBlendEnabled is not null && attachmentIndex < attachmentBlendEnabled.Length)
        {
            attachmentBlendEnabled[attachmentIndex] = null;
        }

        if (attachmentBlendFunc is not null && attachmentIndex < attachmentBlendFunc.Length)
        {
            attachmentBlendFunc[attachmentIndex] = null;
        }
    }

    /// <summary>
    /// Applies any configured per-attachment blend settings using <see cref="StateCache"/>.
    /// </summary>
    public void ApplyAttachmentBlendState()
    {
        var cache = StateCache.Current;

        if (attachmentBlendEnabled is not null)
        {
            int count = attachmentBlendEnabled.Length;
            for (int i = 0; i < count; i++)
            {
                bool? enabled = attachmentBlendEnabled[i];
                if (enabled.HasValue)
                {
                    cache.SetBlendEnabledIndexed(i, enabled.Value);
                }
            }
        }

        if (attachmentBlendFunc is not null)
        {
            int count = attachmentBlendFunc.Length;
            for (int i = 0; i < count; i++)
            {
                GlBlendFunc? func = attachmentBlendFunc[i];
                if (func.HasValue)
                {
                    cache.SetBlendFuncIndexed(i, func.Value);
                }
            }
        }
    }

    #endregion

    #region Private
    /// <summary>Expands cached blend settings without changing GL state.</summary>
    private void EnsureAttachmentBlendCapacity(int attachmentIndex)
    {
        int needed = attachmentIndex + 1;

        if (attachmentBlendEnabled is null || attachmentBlendEnabled.Length < needed)
        {
            int newSize = Math.Max(needed, attachmentBlendEnabled?.Length ?? 0);
            newSize = Math.Max(newSize, 8);
            newSize = Math.Max(newSize, (attachmentBlendEnabled?.Length ?? 0) * 2);

            var newEnabled = new bool?[newSize];
            if (attachmentBlendEnabled is not null)
            {
                Array.Copy(attachmentBlendEnabled, newEnabled, attachmentBlendEnabled.Length);
            }

            attachmentBlendEnabled = newEnabled;
        }

        if (attachmentBlendFunc is null || attachmentBlendFunc.Length < needed)
        {
            int newSize = Math.Max(needed, attachmentBlendFunc?.Length ?? 0);
            newSize = Math.Max(newSize, 8);
            newSize = Math.Max(newSize, (attachmentBlendFunc?.Length ?? 0) * 2);

            var newFunc = new GlBlendFunc?[newSize];
            if (attachmentBlendFunc is not null)
            {
                Array.Copy(attachmentBlendFunc, newFunc, attachmentBlendFunc.Length);
            }

            attachmentBlendFunc = newFunc;
        }
    }

    #endregion


}
