using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns one domain's images and exact metadata without imposing solver or native-input policy.</summary>
internal sealed class CloudVolumeFields : IDisposable
{
    private readonly GpuResourceCollection resources = new();
    internal CloudVolumeDomain Domain { get; set; }
    internal CloudVolumeEncoding Encoding { get; set; }
    /// <summary>Tracks the spare's storage scale independently of the published field.</summary>
    internal float DiffuseSpareScale { get; private set; }
    /// <summary>Certifies that the retained successful block list covers every difference between fields.</summary>
    internal bool DiffuseRepairTracked { get; private set; } = true;
    internal CloudVolumeLightingBounds? LightingBounds { get; set; }
    internal DynamicTexture3D Density { get; }
    internal DynamicTexture3D Direct { get; }
    internal DynamicTexture3D Diffuse { get; private set; }
    internal DynamicTexture3D DiffuseSpare { get; private set; }
    internal DynamicTexture2D Descriptors { get; }
    internal GpuShaderStorageBuffer Regions { get; }
    internal GpuShaderStorageBuffer Occupancy { get; }
    internal GpuShaderStorageBuffer Scratch { get; }
    internal int InitializedBlocks { get; set; }
    internal uint Revision { get; set; }
    internal bool Initialized => InitializedBlocks == Domain.Blocks;
    internal CloudVolumeDensityState DensityState { get; } = new();
    /// <summary>Captures the current coherent view bindings without transferring their ownership.</summary>
    internal CloudVolumeViewFields View => new(Domain,Encoding,LightingBounds,Revision,Density,Direct,Diffuse,Regions,Occupancy);

    #region Public API
    /// <summary>Allocates only the admitted field set, adopting each allocation immediately for failure cleanup.</summary>
    internal CloudVolumeFields(CloudVolumeDomain domain, CloudVolumeEncoding encoding)
    {
        Domain = domain; Encoding = encoding; DiffuseSpareScale = encoding.Scale;
        try
        {
            Density = Volume(PixelInternalFormat.R16f); Direct = Volume(PixelInternalFormat.Rg16f);
            Diffuse = Volume(PixelInternalFormat.Rgba16f); DiffuseSpare = Volume(PixelInternalFormat.Rgba16f);
            Descriptors = resources.Own(DynamicTexture2D.Create(domain.Width, domain.Width, PixelInternalFormat.Rgba32f));
            Descriptors.UploadDataImmediate(new float[4 * domain.Width * domain.Width]);
            Regions = Buffer(domain.Blocks * 16); Occupancy = Buffer(domain.Blocks);
            // Maintenance/copy UBOs total 128 bytes outside this buffer.
            // Remaining parameter/list space stays within the fixed 1 MiB allowance.
            Scratch = Buffer((CloudVolumeDomain.ScratchBytes - 4096) / 4);
            GlDebug.ThrowIfErrors("Cloud volume allocation");
        }
        catch { resources.Dispose(); throw; }
    }

    /// <summary>Publishes a complete destination and its encoding only after the owner's barriers and checks.</summary>
    internal void PublishDiffuse(CloudVolumeEncoding encoding, bool repairTracked = false)
    {
        uint next = checked(Revision + 1);
        DiffuseSpareScale = Encoding.Scale; DiffuseRepairTracked = repairTracked;
        (Diffuse, DiffuseSpare) = (DiffuseSpare, Diffuse); Encoding = encoding; Revision = next;
    }

    /// <summary>Retires images and buffers, including allocations acquired before a later failure.</summary>
    public void Dispose() => resources.Dispose();
    #endregion
    #region Private
    /// <summary>Creates true trilinear volumes with circular horizontal and finite vertical addressing.</summary>
    private DynamicTexture3D Volume(PixelInternalFormat format)
    {
        var texture = resources.Own(DynamicTexture3D.Create(Domain.Size, CloudVolumeDomain.Height, Domain.Size,
            format, textureTarget: TextureTarget.Texture3D));
        texture.SetTexWrap(TextureWrapMode.Repeat, TextureWrapMode.ClampToEdge, TextureWrapMode.Repeat);
        return texture;
    }
    /// <summary>Initializes an exact integer buffer without bypassing the resource owner.</summary>
    private GpuShaderStorageBuffer Buffer(int words)
    {
        var buffer = resources.Own(GpuShaderStorageBuffer.Create()); buffer.UploadData(new uint[words]); return buffer;
    }
    #endregion
}
