using System;
using System.Collections.Generic;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Postprocessing;

/// <summary>Owns independently weighted Gaussian scales in unexposed scene-linear HDR.</summary>
internal sealed class BloomRenderer : IDisposable
{
    // Radii measure one-sided support in each scale's texels. Neutral tints preserve source chromaticity.
    private static readonly (int Radius, Vector3 Tint, float Energy)[] Scales =
    [
        (3, Vector3.One, .25f), (4, Vector3.One, .22f), (5, Vector3.One, .19f),
        (6, Vector3.One, .15f), (7, Vector3.One, .11f), (8, Vector3.One, .08f)
    ];
    private readonly List<PostprocessImage> levels = new(), scratch = new();
    private int width, height, requestedLevels;
    private BloomShaderProgram? shader;
    private GraphicsPipeline? pipeline;

    #region Public API
    /// <summary>Publishes the completed half-resolution sum of all Gaussian scales.</summary>
    internal DynamicTexture2D Texture => scratch[0].Texture;

    /// <summary>Recreates the complete pyramid only when size or configured scale count changes.</summary>
    internal GraphicsPipeline Prepare(ICoreClientAPI api, PostprocessDraw draw, int frameWidth, int frameHeight, int levelCount)
    {
        levelCount = Math.Clamp(levelCount, 1, Scales.Length);
        if (width != frameWidth || height != frameHeight || requestedLevels != levelCount || levels.Count == 0)
        {
            Dispose();
            width = frameWidth; height = frameHeight; requestedLevels = levelCount;
            try
            {
                int w = Math.Max(1, (width + 1) / 2), h = Math.Max(1, (height + 1) / 2);
                for (int i = 0; i < requestedLevels; i++)
                {
                    levels.Add(new(w, h, $"Bloom.Scale.{i}"));
                    scratch.Add(new(w, h, $"Bloom.Scratch.{i}"));
                    if (w == 1 && h == 1) break;
                    w = Math.Max(1, (w + 1) / 2); h = Math.Max(1, (h + 1) / 2);
                }
            }
            catch { Dispose(); throw; }
        }
        shader = GpuShaderPrograms.Get<BloomShaderProgram>(api, "pbr_bloom");
        if (shader?.EnsureReady() != true) throw new InvalidOperationException("Owned bloom shader unavailable.");
        return pipeline = draw.Prepare(shader, levels[0].Target);
    }

    /// <summary>Extracts once, filters independent scales, then accumulates their normalized tinted energy.</summary>
    internal void Render(GraphicsCommandContext commands, PostprocessDraw draw, GpuTexture scene,
        (GpuTexture? Texture, float ManualEV) exposure, PostprocessParameters settings)
    {
        // Finish reduction before overwriting any level with its Gaussian result.
        for (int i = 0; i < levels.Count; i++)
        {
            shader!.SourceImage = i == 0 ? scene : levels[i - 1].Texture;
            shader.SecondaryImage = exposure.Texture ?? scene;
            shader.Capture(new(0, 0, i == 0 ? 0 : 1, exposure.Texture is not null ? 1 : 0),
                new(settings.BloomThreshold, settings.BloomKnee, settings.BloomStrength, exposure.ManualEV));
            draw.Submit(commands, pipeline!, levels[i].Target);
        }
        float totalEnergy = 0;
        for (int i = 0; i < levels.Count; i++)
        {
            totalEnergy += Scales[i].Energy;
            // Each draw reads a different object from its output; no cross-layer feedback or barriers.
            shader!.SecondaryImage = scene;
            shader.SourceImage = levels[i].Texture;
            shader.CaptureGaussian(Scales[i].Radius, vertical: false);
            draw.Submit(commands, pipeline!, scratch[i].Target);
            shader.SourceImage = scratch[i].Texture;
            shader.CaptureGaussian(Scales[i].Radius, vertical: true);
            draw.Submit(commands, pipeline!, levels[i].Target);
        }
        // Reuse horizontal scratch after all filters finish. Every scale enters the sum exactly once.
        for (int i = levels.Count - 1; i >= 0; i--)
        {
            shader!.SourceImage = levels[i].Texture;
            bool hasCoarser = i + 1 < levels.Count;
            shader.SecondaryImage = hasCoarser ? scratch[i + 1].Texture : scene;
            shader.Capture(new(0, 0, 4, hasCoarser ? 1 : 0),
                new(Scales[i].Tint * (Scales[i].Energy / totalEnergy), 0));
            draw.Submit(commands, pipeline!, scratch[i].Target);
        }
    }

    /// <summary>Releases borrowing framebuffers and images; the common draw owner retains executable PSOs.</summary>
    public void Dispose()
    {
        foreach (var image in scratch) image.Dispose();
        foreach (var image in levels) image.Dispose();
        scratch.Clear(); levels.Clear(); shader = null; pipeline = null;
    }
    #endregion
}
