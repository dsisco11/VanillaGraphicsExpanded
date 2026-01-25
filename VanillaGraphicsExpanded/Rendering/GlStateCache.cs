using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Caches OpenGL state to avoid redundant driver calls.
/// </summary>
/// <remarks>
/// Best-effort: correctness assumes state changes flow through this cache.
/// When external code stomps state, callers should use <see cref="InvalidateAll"/> (or a targeted dirty method).
/// </remarks>
internal sealed partial class GlStateCache
{
    [ThreadStatic]
    private static GlStateCache? current;

    public static GlStateCache Current => current ??= new GlStateCache();

    private GlStateCache()
    {
    }

    // Fixed-function knobs (tracked by PSO masks)
    private bool? depthTestEnabled;
    private DepthFunction? depthFunc;
    private bool? depthWriteMask;

    private bool? blendEnabled;
    private GlBlendFunc? blendFunc;

    private bool? cullFaceEnabled;
    private bool? scissorTestEnabled;
    private GlColorMask? colorMask;
    private float? lineWidth;
    private float? pointSize;

    // Indexed blend cache. Entries are null when unknown/dirty.
    private bool?[]? blendEnabledIndexed;
    private GlBlendFunc?[]? blendFuncIndexed;

    public void InvalidateAll()
    {
        depthTestEnabled = null;
        depthFunc = null;
        depthWriteMask = null;

        blendEnabled = null;
        blendFunc = null;

        cullFaceEnabled = null;
        scissorTestEnabled = null;
        colorMask = null;
        lineWidth = null;
        pointSize = null;

        if (blendEnabledIndexed is not null) Array.Fill(blendEnabledIndexed, null);
        if (blendFuncIndexed is not null) Array.Fill(blendFuncIndexed, null);

        InvalidateBindings();
    }

    public void DirtyIndexedBlendFunc()
    {
        if (blendFuncIndexed is not null)
        {
            Array.Fill(blendFuncIndexed, null);
        }
    }

    public void DirtyIndexedBlendEnable()
    {
        if (blendEnabledIndexed is not null)
        {
            Array.Fill(blendEnabledIndexed, null);
        }
    }

    #region PSO Apply

    public void Apply(in GlPipelineDesc desc)
    {
        // Apply order: enables/disables first, then funcs/masks, and global blend before per-RT blend.
        ApplyEnableBit(desc, GlPipelineStateId.DepthTestEnable, EnableCap.DepthTest, ref depthTestEnabled);
        ApplyEnableBit(desc, GlPipelineStateId.CullFaceEnable, EnableCap.CullFace, ref cullFaceEnabled);
        ApplyEnableBit(desc, GlPipelineStateId.ScissorTestEnable, EnableCap.ScissorTest, ref scissorTestEnabled);

        ApplyEnableBit(desc, GlPipelineStateId.BlendEnable, EnableCap.Blend, ref blendEnabled, dirtiesIndexedBlendEnable: true);

        ApplyDepthFunc(desc);
        ApplyDepthWriteMask(desc);

        ApplyBlendFunc(desc);
        ApplyBlendEnableIndexed(desc);
        ApplyBlendFuncIndexed(desc);

        ApplyColorMask(desc);
        ApplyLineWidth(desc);
        ApplyPointSize(desc);
    }

    private void ApplyEnableBit(
        in GlPipelineDesc desc,
        GlPipelineStateId id,
        EnableCap cap,
        ref bool? cache,
        bool dirtiesIndexedBlendEnable = false)
    {
        if (desc.DefaultMask.Contains(id))
        {
            SetEnable(cap, enabled: false, ref cache);
            if (dirtiesIndexedBlendEnable) DirtyIndexedBlendEnable();
            return;
        }

        if (desc.NonDefaultMask.Contains(id))
        {
            SetEnable(cap, enabled: true, ref cache);
            if (dirtiesIndexedBlendEnable) DirtyIndexedBlendEnable();
        }
    }

    private void ApplyDepthFunc(in GlPipelineDesc desc)
    {
        if (desc.DefaultMask.Contains(GlPipelineStateId.DepthFunc))
        {
            SetDepthFunc(DepthFunction.Less);
            return;
        }

        if (desc.NonDefaultMask.Contains(GlPipelineStateId.DepthFunc))
        {
            SetDepthFunc(desc.DepthFunc!.Value);
        }
    }

    private void ApplyDepthWriteMask(in GlPipelineDesc desc)
    {
        if (desc.DefaultMask.Contains(GlPipelineStateId.DepthWriteMask))
        {
            SetDepthWriteMask(true);
            return;
        }

        if (desc.NonDefaultMask.Contains(GlPipelineStateId.DepthWriteMask))
        {
            SetDepthWriteMask(desc.DepthWriteMask!.Value);
        }
    }

    private void ApplyBlendFunc(in GlPipelineDesc desc)
    {
        if (desc.DefaultMask.Contains(GlPipelineStateId.BlendFunc))
        {
            SetBlendFunc(GlBlendFunc.Default);
            return;
        }

        if (desc.NonDefaultMask.Contains(GlPipelineStateId.BlendFunc))
        {
            SetBlendFunc(desc.BlendFunc!.Value);
        }
    }

    private void ApplyBlendEnableIndexed(in GlPipelineDesc desc)
    {
        bool hasIntent =
            desc.DefaultMask.Contains(GlPipelineStateId.BlendEnableIndexed)
            || desc.NonDefaultMask.Contains(GlPipelineStateId.BlendEnableIndexed);

        if (!hasIntent)
        {
            return;
        }

        byte[] attachments = desc.BlendEnableIndexedAttachments!;
        bool enable = desc.NonDefaultMask.Contains(GlPipelineStateId.BlendEnableIndexed);
        bool disable = desc.DefaultMask.Contains(GlPipelineStateId.BlendEnableIndexed);

        for (int i = 0; i < attachments.Length; i++)
        {
            int idx = attachments[i];
            if (enable) SetBlendEnabledIndexed(idx, enabled: true);
            else if (disable) SetBlendEnabledIndexed(idx, enabled: false);
        }
    }

    private void ApplyBlendFuncIndexed(in GlPipelineDesc desc)
    {
        bool hasIntent =
            desc.DefaultMask.Contains(GlPipelineStateId.BlendFuncIndexed)
            || desc.NonDefaultMask.Contains(GlPipelineStateId.BlendFuncIndexed);

        if (!hasIntent)
        {
            return;
        }

        GlBlendFuncIndexed[] values = desc.BlendFuncIndexed!;

        if (desc.DefaultMask.Contains(GlPipelineStateId.BlendFuncIndexed))
        {
            for (int i = 0; i < values.Length; i++)
            {
                SetBlendFuncIndexed(values[i].AttachmentIndex, GlBlendFunc.Default);
            }
            return;
        }

        if (desc.NonDefaultMask.Contains(GlPipelineStateId.BlendFuncIndexed))
        {
            for (int i = 0; i < values.Length; i++)
            {
                SetBlendFuncIndexed(values[i].AttachmentIndex, values[i].BlendFunc);
            }
        }
    }

    private void ApplyColorMask(in GlPipelineDesc desc)
    {
        if (desc.DefaultMask.Contains(GlPipelineStateId.ColorMask))
        {
            SetColorMask(GlColorMask.All);
            return;
        }

        if (desc.NonDefaultMask.Contains(GlPipelineStateId.ColorMask))
        {
            SetColorMask(desc.ColorMask!.Value);
        }
    }

    private void ApplyLineWidth(in GlPipelineDesc desc)
    {
        if (desc.DefaultMask.Contains(GlPipelineStateId.LineWidth))
        {
            SetLineWidth(1f);
            return;
        }

        if (desc.NonDefaultMask.Contains(GlPipelineStateId.LineWidth))
        {
            SetLineWidth(desc.LineWidth!.Value);
        }
    }

    private void ApplyPointSize(in GlPipelineDesc desc)
    {
        if (desc.DefaultMask.Contains(GlPipelineStateId.PointSize))
        {
            SetPointSize(1f);
            return;
        }

        if (desc.NonDefaultMask.Contains(GlPipelineStateId.PointSize))
        {
            SetPointSize(desc.PointSize!.Value);
        }
    }

    #endregion

    #region Fixed-Function Cached Setters

    private static void SetEnable(EnableCap cap, bool enabled, ref bool? cache)
    {
        if (cache.HasValue && cache.Value == enabled)
        {
            return;
        }

        try
        {
            if (enabled) GL.Enable(cap);
            else GL.Disable(cap);
            cache = enabled;
        }
        catch
        {
        }
    }

    public void SetDepthFunc(DepthFunction function)
    {
        if (depthFunc.HasValue && depthFunc.Value == function)
        {
            return;
        }

        try
        {
            GL.DepthFunc(function);
            depthFunc = function;
        }
        catch
        {
        }
    }

    public void SetDepthWriteMask(bool enabled)
    {
        if (depthWriteMask.HasValue && depthWriteMask.Value == enabled)
        {
            return;
        }

        try
        {
            GL.DepthMask(enabled);
            depthWriteMask = enabled;
        }
        catch
        {
        }
    }

    public void SetBlendFunc(GlBlendFunc func)
    {
        if (blendFunc.HasValue && blendFunc.Value == func)
        {
            return;
        }

        try
        {
            GL.BlendFuncSeparate(func.SrcRgb, func.DstRgb, func.SrcAlpha, func.DstAlpha);
            blendFunc = func;

            // glBlendFunc updates the blend func for all draw buffers; indexed cache is now stale.
            DirtyIndexedBlendFunc();
        }
        catch
        {
        }
    }

    public void SetBlendEnabledIndexed(int attachmentIndex, bool enabled)
    {
        if (attachmentIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attachmentIndex), attachmentIndex, "Attachment index must be >= 0.");
        }

        EnsureIndexedBlendCapacity(attachmentIndex);

        bool? cached = blendEnabledIndexed![attachmentIndex];
        if (cached.HasValue && cached.Value == enabled)
        {
            return;
        }

        try
        {
            if (enabled) GL.Enable(IndexedEnableCap.Blend, attachmentIndex);
            else GL.Disable(IndexedEnableCap.Blend, attachmentIndex);
            blendEnabledIndexed[attachmentIndex] = enabled;
        }
        catch
        {
        }
    }

    public void SetBlendFuncIndexed(int attachmentIndex, GlBlendFunc func)
    {
        if (attachmentIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attachmentIndex), attachmentIndex, "Attachment index must be >= 0.");
        }

        EnsureIndexedBlendCapacity(attachmentIndex);

        GlBlendFunc? cached = blendFuncIndexed![attachmentIndex];
        if (cached.HasValue && cached.Value == func)
        {
            return;
        }

        try
        {
            GL.BlendFuncSeparate(attachmentIndex, func.SrcRgb, func.DstRgb, func.SrcAlpha, func.DstAlpha);
            blendFuncIndexed[attachmentIndex] = func;
        }
        catch
        {
        }
    }

    public void SetColorMask(GlColorMask mask)
    {
        if (colorMask.HasValue && colorMask.Value == mask)
        {
            return;
        }

        try
        {
            GL.ColorMask(mask.R, mask.G, mask.B, mask.A);
            colorMask = mask;
        }
        catch
        {
        }
    }

    public void SetLineWidth(float width)
    {
        if (lineWidth.HasValue && Math.Abs(lineWidth.Value - width) < 0.0001f)
        {
            return;
        }

        try
        {
            GL.LineWidth(width);
            lineWidth = width;
        }
        catch
        {
        }
    }

    public void SetPointSize(float size)
    {
        if (pointSize.HasValue && Math.Abs(pointSize.Value - size) < 0.0001f)
        {
            return;
        }

        try
        {
            GL.PointSize(size);
            pointSize = size;
        }
        catch
        {
        }
    }

    private void EnsureIndexedBlendCapacity(int attachmentIndex)
    {
        int needed = attachmentIndex + 1;
        if (blendEnabledIndexed is null || blendEnabledIndexed.Length < needed)
        {
            int newSize = Math.Max(needed, blendEnabledIndexed?.Length ?? 0);
            newSize = Math.Max(newSize, 8);
            newSize = Math.Max(newSize, (blendEnabledIndexed?.Length ?? 0) * 2);

            var newEnabled = new bool?[newSize];
            var newFunc = new GlBlendFunc?[newSize];

            if (blendEnabledIndexed is not null) Array.Copy(blendEnabledIndexed, newEnabled, blendEnabledIndexed.Length);
            if (blendFuncIndexed is not null) Array.Copy(blendFuncIndexed, newFunc, blendFuncIndexed.Length);

            blendEnabledIndexed = newEnabled;
            blendFuncIndexed = newFunc;
        }
    }

    #endregion
}

