using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Applies pipeline declarations through the shared transition backend.</summary>
internal sealed partial class StateCache
{
    [System.ThreadStatic] private static StateCache? current;
    #region Public API
    /// <summary>Returns this thread's cache after checking native context identity.</summary>
    public static StateCache Current { get { var cache = current ??= new StateCache(); cache.SynchronizeContext(); return cache; } }
    /// <summary>Forgets mutable knowledge without changing native state or diagnostic totals.</summary>
    public void InvalidateAll() => Invalidate(EPipelineState.All);
    /// <summary>Applies the fixed-function intents supplied by a compatibility pipeline.</summary>
    public void Apply(in GlPipelineDesc desc)
    {
        SynchronizeContext();
#if DEBUG
        if (!string.IsNullOrWhiteSpace(desc.Name))
        {
            using var group = GlDebug.Group($"PSO.Apply: {desc.Name}");
            ApplyInternal(desc);
            return;
        }
#endif
        ApplyInternal(desc);
    }
    #endregion
    #region Private
    /// <summary>Creates an initially unknown cache.</summary>
    private StateCache() { }

    #region Pipeline application
    /// <summary>Applies global intents before indexed overrides through the shared backend.</summary>
    private void ApplyInternal(in GlPipelineDesc desc)
    {
        // Apply order: enables/disables first, then funcs/masks, and global blend before per-RT blend.
        ApplyEnableBit(desc, GlPipelineStateId.DepthTestEnable, EnableCap.DepthTest);
        ApplyEnableBit(desc, GlPipelineStateId.CullFaceEnable, EnableCap.CullFace);
        ApplyEnableBit(desc, GlPipelineStateId.ScissorTestEnable, EnableCap.ScissorTest);

        ApplyEnableBit(desc, GlPipelineStateId.BlendEnable, EnableCap.Blend);

        ApplyDepthFunc(desc);
        ApplyDepthWriteMask(desc);

        ApplyBlendFunc(desc);
        ApplyBlendEnableIndexed(desc);
        ApplyBlendFuncIndexed(desc);

        ApplyColorMask(desc);
        ApplyLineWidth(desc);
        ApplyPointSize(desc);
    }

    /// <summary>Applies a capability only when its descriptor bit declares intent.</summary>
    private void ApplyEnableBit(in GlPipelineDesc desc, GlPipelineStateId id, EnableCap cap)
    {
        if (desc.DefaultMask.Contains(id)) SetCapability(cap, false);
        else if (desc.NonDefaultMask.Contains(id)) SetCapability(cap, true);
    }

    /// <summary>Applies the declared default or explicit depth comparison.</summary>
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

    /// <summary>Applies the declared default or explicit depth write mask.</summary>
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

    /// <summary>Applies declared factors to all draw outputs.</summary>
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

    /// <summary>Applies enable overrides to the descriptor's output indices.</summary>
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

    /// <summary>Applies factor overrides to the descriptor's output indices.</summary>
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

    /// <summary>Applies the declared global color write mask.</summary>
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

    /// <summary>Applies the declared static line width.</summary>
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

    /// <summary>Applies the declared static point size.</summary>
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
    #endregion
}
