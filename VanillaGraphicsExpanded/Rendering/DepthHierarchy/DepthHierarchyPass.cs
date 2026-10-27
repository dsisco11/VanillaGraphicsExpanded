using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Integration;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns the shared R32F hierarchy and compact staging storage for its coarse tail.</summary>
internal sealed class DepthHierarchyPass : IDisposable
{
    private GpuResourceCollection resources = new();
    private GpuShaderStorageBuffer? tail;
    private DepthHierarchyComputeShader? shader;
    #region Public API
    /// <summary>Gets the owned hierarchy borrowed by AO and indirect lighting.</summary>
    internal DynamicTexture2D? Texture { get; private set; }
#if DEBUG
    /// <summary>Gets logical payload bytes across all hierarchy levels for diagnostics.</summary>
    internal long StorageBytes { get; private set; }
#endif
    /// <summary>Gets the counter and coarse-tail staging bytes.</summary>
    internal int ScratchBytes { get; private set; }
    /// <summary>Allocates persistent storage for an extent and prepares the typed compute executable.</summary>
    internal void Prepare(int width, int height, DepthHierarchyComputeShader shader)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
        if (!shader.EnsureReady()) throw new InvalidOperationException(shader.PreparationLog);
        var binding=shader.ProgramLayout.BinaryInterface!.PreparedBindings.Resolve(
            Contracts.GpuBindingEntry.Identity(Contracts.ShaderBindingKind.StorageBlock,"DepthHierarchyTail"));
        var limits = GpuSupport.Graphics;
        if (!limits.SupportsArbComputeShader || !limits.SupportsArbShaderStorageBufferObject || !limits.SupportsArbShaderImageLoadStore
            || limits.MaxComputeWorkGroupSize.Length < 1 || limits.MaxComputeWorkGroupCount.Length < 2
            || limits.MaxImageUnits < 7 || limits.MaxComputeImageUniforms < 7 || limits.MaxComputeSharedMemorySize < 20484
            || limits.MaxComputeWorkGroupInvocations < 256 || limits.MaxComputeWorkGroupSize[0] < 256)
            throw new InvalidOperationException("Depth hierarchy requires seven compute images, 20484 shared bytes and 256 invocations.");
        if (width > limits.MaxTextureSize || height > limits.MaxTextureSize
            || Math.Max(1, width >> 6) > limits.MaxComputeWorkGroupCount[0]
            || Math.Max(1, height >> 6) > limits.MaxComputeWorkGroupCount[1])
            throw new ArgumentOutOfRangeException(nameof(width), "Depth hierarchy extent exceeds context limits.");
        if (Texture is null || Texture.Width != width || Texture.Height != height)
        {
            Dispose();
            try
            {
                int levels = 1;
                for (int extent = Math.Max(width, height); extent > 1; extent >>= 1) levels++;
                Texture = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), MipLevels = levels, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (levels) - 1, Label = "DepthHierarchy" }).RequireResource());
                ScratchBytes = 4;
                for (int level = 0; level < levels; level++)
                {
                    int bytes = checked(4 * Math.Max(1, width >> level) * Math.Max(1, height >> level));
#if DEBUG
                    StorageBytes += bytes;
#endif
                    if (level > 6) ScratchBytes += bytes;
                }
                // Include the linked block minimum even when this extent never writes a coarse-tail element.
                ScratchBytes=Math.Max(ScratchBytes,binding.MinimumSize);
                if (ScratchBytes > limits.MaxShaderStorageBlockSize)
                    throw new InvalidOperationException("Depth hierarchy tail exceeds the storage-block limit.");
                tail = resources.Own(RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "DepthHierarchy.Tail" }).RequireResource());
                tail.UploadData(new uint[ScratchBytes / 4]);
            }
            catch { Dispose(); throw; }
        }
        this.shader = shader;
        shader.SetExtent(width, height, Texture.MipLevels);
        shader.Tail = tail;
        shader.Mip0 = new RHIImageBinding(Texture, ImageAccess.ReadWrite, Math.Min(0, Texture.MipLevels - 1));
        shader.Mip1 = new RHIImageBinding(Texture, ImageAccess.ReadWrite, Math.Min(1, Texture.MipLevels - 1));
        shader.Mip2 = new RHIImageBinding(Texture, ImageAccess.ReadWrite, Math.Min(2, Texture.MipLevels - 1));
        shader.Mip3 = new RHIImageBinding(Texture, ImageAccess.ReadWrite, Math.Min(3, Texture.MipLevels - 1));
        shader.Mip4 = new RHIImageBinding(Texture, ImageAccess.ReadWrite, Math.Min(4, Texture.MipLevels - 1));
        shader.Mip5 = new RHIImageBinding(Texture, ImageAccess.ReadWrite, Math.Min(5, Texture.MipLevels - 1));
        shader.Mip6 = new RHIImageBinding(Texture, ImageAccess.ReadWrite, Math.Min(6, Texture.MipLevels - 1));
    }
    /// <summary>Generates every mip in one dispatch, transfers only the coarse tail and publishes sampling visibility.</summary>
    internal void Render(GpuTexture depth)
    {
        var texture = Texture ?? throw new InvalidOperationException("Depth hierarchy is not prepared.");
        var program = shader ?? throw new InvalidOperationException("Depth hierarchy executable is missing.");
        program.PrimaryDepth = depth;
        var commands=RHICommandContext.Current;
        using var scope=commands.EnterBoundary(new("DepthHierarchy",true,compute:[program.Pipeline]));
        scope.Run(()=>
            {
                int boundary = Math.Min(6, texture.MipLevels - 1);
                program.Dispatch(Math.Max(1, texture.Width >> boundary), Math.Max(1, texture.Height >> boundary));
                // Image writes feed samplers; storage writes feed PBO transfers and the next counter reuse.
                RHICommandContext.Current.Barrier(new(ResourceAccess.Image | ResourceAccess.ShaderStorage
                    | ResourceAccess.PixelTransfer | ResourceAccess.TextureFetch | ResourceAccess.TextureUpdate));
                if (texture.MipLevels > 7) TransferTail(texture);
            });
    }
    /// <summary>Retires staging storage and shared texture together before a resize or executable reload.</summary>
    public void Dispose()
    {
        resources.Dispose(); resources = new(); tail = null; Texture = null;
#if DEBUG
        StorageBytes = 0;
#endif
        ScratchBytes = 0; shader = null;
    }
    #endregion
    #region Private
    /// <summary>Copies the tiny coarse tail from GPU storage without mapping or CPU readback.</summary>
    private void TransferTail(DynamicTexture2D texture)
    {

        int offset = 4;
        for (int level = 7; level < texture.MipLevels; level++)
        {
            int width = Math.Max(1, texture.Width >> level), height = Math.Max(1, texture.Height >> level);
            RHICommandContext.Current.TransferBufferToTexture(texture, new RHITextureTransfer(tail!, offset, new(new(level), 0, 0, 0, width, height), new(PixelChannelLayout.Red, PixelComponentType.Float)));
            offset += width * height * 4;
        }
    }
    #endregion
}
