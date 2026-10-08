using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Owns a layered linear-radiance allocation and its three-output framebuffer.</summary>
internal sealed class DirectLightingTargets : IDisposable
{
    private readonly GpuResourceCollection resources = new();

    #region Public API
    /// <summary>Names the diffuse, specular and emission images within the radiance array.</summary>
    public const int DiffuseLayer = 0, SpecularLayer = 1, EmissiveLayer = 2;
    /// <summary>Supplies all three lighting contributions through one sampler.</summary>
    public Texture3D Radiance { get; }
    /// <summary>Routes independent fragment outputs to their corresponding array layers.</summary>
    public GpuFramebuffer Framebuffer { get; }
    /// <summary>Reports whether the allocation and its framebuffer remain live.</summary>
    public bool IsValid => Framebuffer.IsValid && Radiance.IsValid;

    /// <summary>Allocates the shared storage before borrowing each layer for MRT rendering.</summary>
    public DirectLightingTargets(int width, int height)
    {
        try
        {
            Radiance = resources.Own(Texture3D.Create(width, height, 3, PixelInternalFormat.Rgba16f,
                TextureFilterMode.Linear, TextureTarget.Texture2DArray, "DirectLighting.Radiance"));
            var layers = new GpuFramebufferAttachment[3];
            for (int layer = 0; layer < layers.Length; layer++)
                layers[layer] = resources.Own(GpuFramebufferAttachment.FromTexture(Radiance, layer: layer));
            Framebuffer = resources.Own(GpuFramebuffer.Create(layers, debugName: "DirectLightingFBO"));
            if (!Framebuffer.CheckStatus(out string? error)) throw new InvalidOperationException(error);
        }
        catch { resources.Dispose(); throw; }
    }

    /// <summary>Retires the framebuffer and layer attachments before their shared storage.</summary>
    public void Dispose() => resources.Dispose();
    #endregion
}
