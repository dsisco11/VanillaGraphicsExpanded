using System.Collections.Generic;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Diagnostics;

/// <summary>Names borrowed engine framebuffer resources without changing their bindings or ownership.</summary>
internal static class EngineFramebufferDebugLabels
{
    #region Public API
    /// <summary>Labels a completed custom framebuffer and its declared textures.</summary>
    [Conditional("DEBUG")]
    internal static void Apply(FrameBufferRef framebuffer, string name)
    {
        if (framebuffer is null || framebuffer.Disposed) return;
        Label(framebuffer, name, new HashSet<int>());
    }

    /// <summary>Labels a newly built default table, keeping shared textures named after their first owner.</summary>
    [Conditional("DEBUG")]
    internal static void ApplyDefaults(IReadOnlyList<FrameBufferRef> framebuffers)
    {
        var textures = new HashSet<int>();
        for (int i = 0; i < framebuffers.Count; i++)
        {
            var framebuffer = framebuffers[i];
            if (framebuffer is null || framebuffer.Disposed) continue;
            string name = i == (int)EnumFrameBuffer.Transparent ? "VS.OIT" : $"VS.{(EnumFrameBuffer)i}";
            Label(framebuffer, name, textures);
        }
    }

    /// <summary>Labels the later bucket attachments directly; the engine table still retains the replaced color ID.</summary>
    [Conditional("DEBUG")]
    internal static void ApplyOit(FrameBufferRef framebuffer, int reveal, int accumulation)
    {
        if (framebuffer is null || framebuffer.Disposed) return;
        GlDebug.TryLabel(ObjectLabelIdentifier.Framebuffer, framebuffer.FboId, "VS.OIT.Framebuffer");
        GlDebug.TryLabel(ObjectLabelIdentifier.Texture, reveal, "VS.OIT.BucketRevealage");
        // All three attachment layers belong to one texture object and therefore share one label.
        GlDebug.TryLabel(ObjectLabelIdentifier.Texture, accumulation, "VS.OIT.AccumulationBuckets");
        var colors = framebuffer.ColorTextureIds;
        if (colors is { Length: > 1 }) GlDebug.TryLabel(ObjectLabelIdentifier.Texture, colors[1], "VS.OIT.Revealage");
        if (colors is { Length: > 2 }) GlDebug.TryLabel(ObjectLabelIdentifier.Texture, colors[2], "VS.OIT.Glow");
    }
    #endregion

    #region Private
    /// <summary>Names shared depth once per default-table traversal rather than renaming it for each consumer.</summary>
    private static void Label(FrameBufferRef framebuffer, string name, HashSet<int> textures)
    {
        GlDebug.TryLabel(ObjectLabelIdentifier.Framebuffer, framebuffer.FboId, $"{name}.Framebuffer");
        if (framebuffer.DepthTextureId != 0 && textures.Add(framebuffer.DepthTextureId))
            GlDebug.TryLabel(ObjectLabelIdentifier.Texture, framebuffer.DepthTextureId, $"{name}.Depth");
        var colors = framebuffer.ColorTextureIds;
        if (colors is null) return;
        for (int i = 0; i < colors.Length; i++)
        {
            if (colors[i] == 0 || !textures.Add(colors[i])) continue;
            string role = name switch
            {
                "VS.OIT" => i switch { 0 => "InitialAccumulation", 1 => "Revealage", 2 => "Glow", _ => $"Color{i}" },
                "VS.Primary" => i switch { 0 => "Color", 1 => "Glow", 2 => "Normal", 3 => "Position", _ => $"Color{i}" },
                _ => $"Color{i}"
            };
            GlDebug.TryLabel(ObjectLabelIdentifier.Texture, colors[i], $"{name}.{role}");
        }
    }
    #endregion
}
