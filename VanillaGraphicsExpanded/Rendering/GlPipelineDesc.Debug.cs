using System.Text;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

internal readonly partial struct GlPipelineDesc
{
    public override string ToString()
    {
#if DEBUG
        return GlPipelineDescDebug.Format(this);
#else
        return $"{nameof(GlPipelineDesc)}(default={DefaultMask}, nonDefault={NonDefaultMask}, name={Name ?? "<null>"})";
#endif
    }
}

internal static class GlPipelineDescDebug
{
    public static string Format(in GlPipelineDesc desc)
    {
        var sb = new StringBuilder(256);

        sb.Append("GlPipelineDesc(");
        if (!string.IsNullOrWhiteSpace(desc.Name))
        {
            sb.Append("name=").Append(desc.Name).Append(", ");
        }

        sb.Append("default=");
        AppendMask(sb, desc.DefaultMask);
        sb.Append(", nonDefault=");
        AppendMask(sb, desc.NonDefaultMask);

        AppendKnob(sb, desc);

        sb.Append(')');
        return sb.ToString();
    }

    private static void AppendMask(StringBuilder sb, GlPipelineStateMask mask)
    {
        sb.Append(mask);
        sb.Append(" [");

        bool first = true;
        for (int i = 0; i < (int)GlPipelineStateId.Count; i++)
        {
            var id = (GlPipelineStateId)i;
            if (!mask.Contains(id)) continue;

            if (!first) sb.Append(", ");
            first = false;
            sb.Append(id);
        }

        sb.Append(']');
    }

    private static void AppendKnob(StringBuilder sb, in GlPipelineDesc desc)
    {
        AppendEnableBit(sb, desc, GlPipelineStateId.DepthTestEnable, "DepthTest", defaultValue: "off", nonDefaultValue: "on");
        AppendValue(sb, desc, GlPipelineStateId.DepthFunc, "DepthFunc", defaultValue: DepthFunction.Less, value: desc.DepthFunc);
        AppendValue(sb, desc, GlPipelineStateId.DepthWriteMask, "DepthWriteMask", defaultValue: true, value: desc.DepthWriteMask);

        AppendEnableBit(sb, desc, GlPipelineStateId.BlendEnable, "Blend", defaultValue: "off", nonDefaultValue: "on");
        AppendValue(sb, desc, GlPipelineStateId.BlendFunc, "BlendFunc", defaultValue: GlBlendFunc.Default, value: desc.BlendFunc);

        AppendIndexedEnable(sb, desc, GlPipelineStateId.BlendEnableIndexed, "BlendIndexed");
        AppendIndexedBlendFunc(sb, desc, GlPipelineStateId.BlendFuncIndexed, "BlendFuncIndexed");

        AppendEnableBit(sb, desc, GlPipelineStateId.CullFaceEnable, "CullFace", defaultValue: "off", nonDefaultValue: "on");
        AppendEnableBit(sb, desc, GlPipelineStateId.ScissorTestEnable, "ScissorTest", defaultValue: "off", nonDefaultValue: "on");

        AppendValue(sb, desc, GlPipelineStateId.ColorMask, "ColorMask", defaultValue: GlColorMask.All, value: desc.ColorMask);

        AppendValue(sb, desc, GlPipelineStateId.LineWidth, "LineWidth", defaultValue: 1f, value: desc.LineWidth);
        AppendValue(sb, desc, GlPipelineStateId.PointSize, "PointSize", defaultValue: 1f, value: desc.PointSize);
    }

    private static void AppendEnableBit(
        StringBuilder sb,
        in GlPipelineDesc desc,
        GlPipelineStateId id,
        string label,
        string defaultValue,
        string nonDefaultValue)
    {
        if (desc.DefaultMask.Contains(id))
        {
            sb.Append(", ").Append(label).Append('=').Append(defaultValue);
            return;
        }

        if (desc.NonDefaultMask.Contains(id))
        {
            sb.Append(", ").Append(label).Append('=').Append(nonDefaultValue);
        }
    }

    private static void AppendValue<T>(
        StringBuilder sb,
        in GlPipelineDesc desc,
        GlPipelineStateId id,
        string label,
        T defaultValue,
        T? value)
        where T : struct
    {
        if (desc.DefaultMask.Contains(id))
        {
            sb.Append(", ").Append(label).Append('=').Append(defaultValue);
            return;
        }

        if (desc.NonDefaultMask.Contains(id))
        {
            sb.Append(", ").Append(label).Append('=').Append(value?.ToString() ?? "<null>");
        }
    }

    private static void AppendIndexedEnable(StringBuilder sb, in GlPipelineDesc desc, GlPipelineStateId id, string label)
    {
        bool isDefault = desc.DefaultMask.Contains(id);
        bool isNonDefault = desc.NonDefaultMask.Contains(id);
        if (!isDefault && !isNonDefault) return;

        byte[] attachments = desc.BlendEnableIndexedAttachments ?? [];

        sb.Append(", ").Append(label).Append('[');
        for (int i = 0; i < attachments.Length; i++)
        {
            if (i != 0) sb.Append(',');
            sb.Append(attachments[i]);
        }
        sb.Append(']');
        sb.Append('=').Append(isNonDefault ? "on" : "off");
    }

    private static void AppendIndexedBlendFunc(StringBuilder sb, in GlPipelineDesc desc, GlPipelineStateId id, string label)
    {
        bool isDefault = desc.DefaultMask.Contains(id);
        bool isNonDefault = desc.NonDefaultMask.Contains(id);
        if (!isDefault && !isNonDefault) return;

        GlBlendFuncIndexed[] values = desc.BlendFuncIndexed ?? [];

        sb.Append(", ").Append(label).Append('=');
        if (isDefault)
        {
            sb.Append("default(").Append(GlBlendFunc.Default).Append(')');
            return;
        }

        sb.Append('{');
        for (int i = 0; i < values.Length; i++)
        {
            if (i != 0) sb.Append(", ");
            sb.Append(values[i].AttachmentIndex).Append(':').Append(values[i].BlendFunc);
        }
        sb.Append('}');
    }
}

