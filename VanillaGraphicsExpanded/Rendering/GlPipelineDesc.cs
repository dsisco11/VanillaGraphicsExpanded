using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Phase 2 PSO descriptor: intent bitsets plus compact value payloads for non-default knobs.
/// </summary>
/// <remarks>
/// This intentionally stores only values needed for the bits present in the intent masks.
/// (Some knobs, like indexed blend, also require attachment indices when forcing baseline defaults.)
/// </remarks>
internal readonly partial struct GlPipelineDesc
{
    public string? Name { get; }

    public GlPipelineStateMask DefaultMask { get; }
    public GlPipelineStateMask NonDefaultMask { get; }

    // Depth
    public DepthFunction? DepthFunc { get; }
    public bool? DepthWriteMask { get; }

    // Blending (global)
    public GlBlendFunc? BlendFunc { get; }

    // Blending (indexed/MRT)
    public byte[]? BlendEnableIndexedAttachments { get; }
    public GlBlendFuncIndexed[]? BlendFuncIndexed { get; }

    // Output write masks
    public GlColorMask? ColorMask { get; }

    // Debug-only
    public float? LineWidth { get; }
    public float? PointSize { get; }

    public GlPipelineDesc(
        GlPipelineStateMask defaultMask,
        GlPipelineStateMask nonDefaultMask,
        DepthFunction? depthFunc = null,
        bool? depthWriteMask = null,
        GlBlendFunc? blendFunc = null,
        byte[]? blendEnableIndexedAttachments = null,
        GlBlendFuncIndexed[]? blendFuncIndexed = null,
        GlColorMask? colorMask = null,
        float? lineWidth = null,
        float? pointSize = null,
        string? name = null,
        bool validate = true)
    {
        Name = name;
        DefaultMask = defaultMask;
        NonDefaultMask = nonDefaultMask;

        DepthFunc = depthFunc;
        DepthWriteMask = depthWriteMask;

        BlendFunc = blendFunc;

        BlendEnableIndexedAttachments = CopyAndSort(blendEnableIndexedAttachments);
        BlendFuncIndexed = CopyAndSort(blendFuncIndexed);

        ColorMask = colorMask;

        LineWidth = lineWidth;
        PointSize = pointSize;

#if DEBUG
        if (validate)
        {
            GlPipelineStateValidation.ValidateDesc(this);
        }
#endif
    }

    private static byte[]? CopyAndSort(byte[]? values)
    {
        if (values is null) return null;
        if (values.Length == 0) return Array.Empty<byte>();

        var copy = (byte[])values.Clone();
        Array.Sort(copy);
        return copy;
    }

    private static GlBlendFuncIndexed[]? CopyAndSort(GlBlendFuncIndexed[]? values)
    {
        if (values is null) return null;
        if (values.Length == 0) return Array.Empty<GlBlendFuncIndexed>();

        var copy = (GlBlendFuncIndexed[])values.Clone();
        Array.Sort(copy, static (a, b) => a.AttachmentIndex.CompareTo(b.AttachmentIndex));
        return copy;
    }
}
