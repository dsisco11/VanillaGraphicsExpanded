using VanillaGraphicsExpanded.Rendering.RHI;
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
    internal CloudVolumeViewFields View => new(Domain,Encoding,LightingBounds,Revision,Density,Direct,Diffuse,Regions,Occupancy,this);

    #region Public API
    /// <summary>Allocates only the admitted field set, adopting each allocation immediately for failure cleanup.</summary>
    internal CloudVolumeFields(CloudVolumeDomain domain, CloudVolumeEncoding encoding)
    {
        Domain = domain; Encoding = encoding; DiffuseSpareScale = encoding.Scale;
        try
        {
            Density = Volume(TextureFormat.R16f); Direct = Volume(TextureFormat.Rg16f);
            Diffuse = Volume(TextureFormat.Rgba16f); DiffuseSpare = Volume(TextureFormat.Rgba16f);
            Descriptors = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, domain.Width), Height = System.Math.Max(1, domain.Width), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());
            Descriptors.UploadDataImmediate(new float[4 * domain.Width * domain.Width]);
            Regions = Buffer(domain.Blocks * 16); Occupancy = Buffer(domain.Blocks);
            // Maintenance/copy UBOs total 128 bytes outside this buffer.
            // Remaining parameter/list space stays within the fixed 1 MiB allowance.
            Scratch = Buffer((CloudVolumeDomain.ScratchBytes - 4096) / 4);
            RHICommandContext.Current.CheckDiagnosticErrors("Cloud volume allocation");
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
    /// <summary>Creates 3D fields with circular horizontal and finite vertical addressing; transport fetches exact cells.</summary>
    private DynamicTexture3D Volume(TextureFormat format)
    {
        var texture = resources.Own(RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = format, Width = System.Math.Max(1, Domain.Size), Height = System.Math.Max(1, CloudVolumeDomain.Height), Depth = System.Math.Max(1, Domain.Size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());
        texture.SetTexWrap(SamplerAddressMode.Repeat, SamplerAddressMode.ClampToEdge, SamplerAddressMode.Repeat);
        return texture;
    }
    /// <summary>Initializes an exact integer buffer without bypassing the resource owner.</summary>
    private GpuShaderStorageBuffer Buffer(int words)
    {
        var buffer = resources.Own(RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource()); buffer.UploadData(new uint[words]); return buffer;
    }
    #endregion
}
