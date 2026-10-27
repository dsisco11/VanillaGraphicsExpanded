using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Owns layered linear radiance and declares three independent fragment-output image selections.</summary>
internal sealed class DirectLightingTargets : IDisposable
{
    private readonly GpuResourceCollection resources = new();

    #region Public API
    /// <summary>Names the diffuse, specular and emission images within the radiance array.</summary>
    public const int DiffuseLayer = 0, SpecularLayer = 1, EmissiveLayer = 2;
    /// <summary>Supplies all three lighting contributions through one sampler.</summary>
    public Texture3D Radiance { get; }
    /// <summary>Retains the zero-cleared lighting pass independently of executable reloads.</summary>
    internal RHIRenderPassDescription Pass { get; }
    /// <summary>Reports whether the owned radiance allocation remains live.</summary>
    public bool IsValid => Radiance.IsValid;

    /// <summary>Allocates the shared storage before borrowing each layer for MRT rendering.</summary>
    public DirectLightingTargets(int width, int height)
    {
        try
        {
            Radiance = resources.Own(RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), Depth = System.Math.Max(1, 3), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = "DirectLighting.Radiance" }).RequireResource());
            var outputs = new RHIColorAttachment[3];
            for (int layer = 0; layer < outputs.Length; layer++)
                outputs[layer] = new(Image: RHIAttachment.FromTexture(Radiance, new(Layer: layer)),
                    Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0));
            Pass = new(outputs, label: "DirectLighting");
        }
        catch { resources.Dispose(); throw; }
    }

    /// <summary>Retires shared storage through its owner; the backend retires dependent framebuffer realizations.</summary>
    public void Dispose() => resources.Dispose();
    #endregion
}
