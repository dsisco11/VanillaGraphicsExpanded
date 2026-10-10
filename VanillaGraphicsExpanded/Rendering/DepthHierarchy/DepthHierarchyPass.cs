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
    /// <summary>Gets logical payload bytes across all hierarchy levels.</summary>
    internal long StorageBytes { get; private set; }
    /// <summary>Gets the counter and coarse-tail staging bytes.</summary>
    internal int ScratchBytes { get; private set; }
    /// <summary>Allocates persistent storage for an extent and prepares the typed compute executable.</summary>
    internal void Prepare(int width, int height, DepthHierarchyComputeShader shader)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
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
                Texture = resources.Own(DynamicTexture2D.CreateMipmapped(width, height, PixelInternalFormat.R32f, levels, "DepthHierarchy"));
                ScratchBytes = 4;
                for (int level = 0; level < levels; level++)
                {
                    int bytes = checked(4 * Math.Max(1, width >> level) * Math.Max(1, height >> level));
                    StorageBytes += bytes;
                    if (level > 6) ScratchBytes += bytes;
                }
                if (ScratchBytes > limits.MaxShaderStorageBlockSize)
                    throw new InvalidOperationException("Depth hierarchy tail exceeds the storage-block limit.");
                tail = resources.Own(GpuShaderStorageBuffer.Create(debugName: "DepthHierarchy.Tail"));
                tail.UploadData(new uint[ScratchBytes / 4]);
            }
            catch { Dispose(); throw; }
        }
        this.shader = shader;
        if (!shader.EnsureReady()) throw new InvalidOperationException(shader.PreparationLog);
        shader.SetExtent(width, height, Texture.MipLevels);
        shader.Tail = tail;
        shader.Mip0 = new GpuTextureBinding(Texture, TextureAccess.ReadWrite, Math.Min(0, Texture.MipLevels - 1));
        shader.Mip1 = new GpuTextureBinding(Texture, TextureAccess.ReadWrite, Math.Min(1, Texture.MipLevels - 1));
        shader.Mip2 = new GpuTextureBinding(Texture, TextureAccess.ReadWrite, Math.Min(2, Texture.MipLevels - 1));
        shader.Mip3 = new GpuTextureBinding(Texture, TextureAccess.ReadWrite, Math.Min(3, Texture.MipLevels - 1));
        shader.Mip4 = new GpuTextureBinding(Texture, TextureAccess.ReadWrite, Math.Min(4, Texture.MipLevels - 1));
        shader.Mip5 = new GpuTextureBinding(Texture, TextureAccess.ReadWrite, Math.Min(5, Texture.MipLevels - 1));
        shader.Mip6 = new GpuTextureBinding(Texture, TextureAccess.ReadWrite, Math.Min(6, Texture.MipLevels - 1));
    }
    /// <summary>Generates every mip in one dispatch, transfers only the coarse tail and publishes sampling visibility.</summary>
    internal void Render(GpuTexture depth)
    {
        var texture = Texture ?? throw new InvalidOperationException("Depth hierarchy is not prepared.");
        var program = shader ?? throw new InvalidOperationException("Depth hierarchy executable is missing.");
        program.PrimaryDepth = depth;
        var prepared = program.ProgramLayout.BinaryInterface?.PreparedBindings
            ?? throw new InvalidOperationException("Depth hierarchy bindings are unavailable.");
        if (!EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration("DepthHierarchy"),
            EngineBoundaryResources.From(prepared), _ =>
            {
                int boundary = Math.Min(6, texture.MipLevels - 1);
                program.Dispatch(Math.Max(1, texture.Width >> boundary), Math.Max(1, texture.Height >> boundary));
                // Image writes feed samplers; storage writes feed PBO transfers and the next counter reuse.
                GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit
                    | MemoryBarrierFlags.PixelBufferBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.TextureUpdateBarrierBit);
                if (texture.MipLevels > 7) TransferTail(texture);
            })) throw new InvalidOperationException("Depth hierarchy compute boundary rejected.");
    }
    /// <summary>Retires staging storage and shared texture together before a resize or executable reload.</summary>
    public void Dispose()
    {
        resources.Dispose(); resources = new(); tail = null; Texture = null;
        StorageBytes = 0; ScratchBytes = 0; shader = null;
    }
    #endregion
    #region Private
    /// <summary>Copies the tiny coarse tail from GPU storage without mapping or CPU readback.</summary>
    private void TransferTail(DynamicTexture2D texture)
    {
        var cache = StateCache.Current;
        using var unpack = cache.SetPixelUnpackScope(new StateCache.PixelUnpackState(4));
        using var buffer = cache.BindBufferScope(BufferTarget.PixelUnpackBuffer, tail!.BufferId);
        using var binding = cache.BindTextureScope(TextureTarget.Texture2D, 0, texture.TextureId);
        int offset = 4;
        for (int level = 7; level < texture.MipLevels; level++)
        {
            int width = Math.Max(1, texture.Width >> level), height = Math.Max(1, texture.Height >> level);
            GL.TexSubImage2D(TextureTarget.Texture2D, level, 0, 0, width, height, PixelFormat.Red, PixelType.Float, (IntPtr)offset);
            offset += width * height * 4;
        }
    }
    #endregion
}
