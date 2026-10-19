using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Owns finite candidate storage and publishes only initialized, barrier-complete field sets.</summary>
internal sealed class LightingVolumeStorage(Func<LightingVolumeMaintenanceProgram> createProgram) : IDisposable
{
    private GpuResourceCollection resources = new();
    private LightingVolumeMaintenanceProgram? program;
    private DynamicTexture3D? direct, directSpare, diffuse, diffuseSpare;
    private GpuShaderStorageBuffer? regions, regionSpare;
    private object? owner;
    private long world, allocation, shader;
    private CloudMapStorage nativeStorage;
    private ulong executable;
    private (nint Handle, long Generation) context;
    private bool disposed;
    internal LightingVolumeDomain? Domain { get; private set; }
    internal LightingVolumeEncoding Encoding { get; private set; }
    internal uint Generation { get; private set; }
    internal bool Ready { get; private set; }
    internal DynamicTexture3D Direct => Ready ? direct! : throw new InvalidOperationException("Lighting storage is unavailable.");
    internal DynamicTexture3D Diffuse => Ready ? diffuse! : throw new InvalidOperationException("Lighting storage is unavailable.");
    internal GpuShaderStorageBuffer Regions => Ready ? regions! : throw new InvalidOperationException("Lighting storage is unavailable.");
    internal DynamicTexture2D? PreviousObservation { get; private set; }
    internal DynamicTexture2D? HardReference { get; private set; }
    internal GpuShaderStorageBuffer? Scratch { get; private set; }
    internal long StorageBytes => Domain?.Bytes ?? 0;

    #region Public API
    /// <summary>Admits capabilities before allocation and retains only compatible overlapping material nodes.</summary>
    internal void Ensure(CloudMapInput input, LightingVolumeEncoding encoding, Func<bool> current)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try
        {
            var next = new LightingVolumeDomain(input.Storage.Width, input.Placement);
            if (!encoding.IsValid) throw new ArgumentOutOfRangeException(nameof(encoding));
            program ??= createProgram();
            if (!program.EnsureReady()) throw new InvalidOperationException(program.PreparationLog);
            Validate(next, GpuSupport.Graphics);
            ValidateFormats();
            if (!current()) throw new InvalidOperationException("Native publication changed before storage admission.");
            ulong revision = ((IGpuProgram)program).ExecutableRevision;
            bool compatible = Ready && ReferenceEquals(owner, input.Owner) && world == input.World &&
                allocation == input.Allocation && shader == input.Shader && nativeStorage == input.Storage &&
                executable == revision && context == RenderContextRegistry.Current() && Domain!.Overlaps(next);
            if (!compatible)
            {
                // Retire first, so a replacement never doubles the declared allocation ceiling.
                Retire();
                Generation = checked(Generation + 1);
                Allocate(next);
                Run(next, null, encoding, encoding, 0);
                Swap();
                Run(next, null, encoding, encoding, 0);
                Encoding = encoding;
            }
            else if (Domain!.X != next.X || Domain.Z != next.Z)
            {
                Run(next, Domain, Encoding, Encoding, 1);
                // Validate every input before exposing any of the three remapped destinations.
                if (!current()) throw new InvalidOperationException("Native publication changed during remapping.");
                Swap();
            }
            if (!current() || !program.IsValid || revision != ((IGpuProgram)program).ExecutableRevision)
                throw new InvalidOperationException("Lighting initialization lost its input or executable certificate.");
            Domain = next; owner = input.Owner; world = input.World; allocation = input.Allocation;
            shader = input.Shader; nativeStorage = input.Storage; executable = revision; context = RenderContextRegistry.Current();
            Ready = true;
        }
        catch
        {
            Retire();
            throw;
        }
    }

    /// <summary>Copies a complete field with a monotonic scale, publishing the texture and scale atomically.</summary>
    internal bool CopyDiffuse(LightingVolumeEncoding target, Func<bool> current)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Ready) throw new InvalidOperationException("Lighting storage is unavailable.");
        if (!target.IsValid || target.Scale < Encoding.Scale || target.Cap < Encoding.Cap) throw new ArgumentOutOfRangeException(nameof(target));
        try
        {
            if (!current()) throw new InvalidOperationException("Diffuse copy lost its publication certificate.");
            Run(Domain!, Domain, Encoding, target, 2);
            if (!current() || !program!.IsValid || executable != ((IGpuProgram)program).ExecutableRevision)
                throw new InvalidOperationException("Diffuse copy lost its input or executable certificate.");
            (diffuse, diffuseSpare) = (diffuseSpare, diffuse);
            Encoding = target;
            return true;
        }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            // Old texture/scale stay paired, but the failed frame may not sample them.
            Ready = false;
            return false;
        }
    }

    /// <summary>Rejects unsupported fixed work shapes and payloads without allocating resources.</summary>
    internal static void Validate(LightingVolumeDomain domain, GraphicsCapabilities caps)
    {
        if (!caps.SupportsArbComputeShader || !caps.SupportsArbShaderStorageBufferObject ||
            !caps.SupportsArbShaderImageLoadStore || !caps.SupportsInternalFormatQuery2 ||
            domain.Size > caps.Max3DTextureSize || LightingVolumeDomain.Height > caps.Max3DTextureSize ||
            domain.Width > caps.MaxTextureSize || caps.MaxImageUnits < 2 || caps.MaxComputeImageUniforms < 2 ||
            caps.MaxCombinedTextureImageUnits < 2 || caps.MaxComputeTextureImageUnits < 2 ||
            caps.MaxUniformBufferBindings < 1 || caps.MaxUniformBlockSize < 96 ||
            caps.MaxComputeShaderStorageBlocks < 2 || caps.MaxShaderStorageBufferBindings < 2 ||
            caps.MaxShaderStorageBlockSize < Math.Max(64L * domain.Regions, 65280) ||
            caps.MaxComputeWorkGroupInvocations < 64 || caps.MaxComputeWorkGroupSize.Length < 3 ||
            caps.MaxComputeWorkGroupCount.Length < 3 || domain.Bytes > 32L * 1024 * 1024)
            throw new NotSupportedException("Cloud lighting volume storage exceeds context capabilities.");
        for (int axis = 0; axis < 3; axis++)
            if (caps.MaxComputeWorkGroupSize[axis] < 4 || caps.MaxComputeWorkGroupCount[axis] <
                ((axis == 1 ? LightingVolumeDomain.Height : domain.Size) + 3) / 4)
                throw new NotSupportedException("Cloud lighting volume dispatch exceeds context capabilities.");
    }

    /// <summary>Revokes storage and executable state at explicit lifecycle recovery boundaries.</summary>
    internal void Reset()
    {
        RenderingCleanup.Run(Retire, () => { program?.Dispose(); program = null; });
    }

    /// <summary>Permanently retires every candidate-owned allocation and executable.</summary>
    public void Dispose() { if (disposed) return; disposed = true; Reset(); }
    #endregion

    #region Private
    #region Allocation and capability admission
    /// <summary>Checks mandatory target-specific formats before creating owned images.</summary>
    private static void ValidateFormats()
    {
        foreach (var format in new[] { SizedInternalFormat.Rg16f, SizedInternalFormat.Rgba16f })
        {
            var support = GpuSupport.GetInternalFormatCapabilities(ImageTarget.Texture3D, format);
            if (!support.Supported || support.ImageLoadSupport == 0 || support.ImageStoreSupport == 0 || support.FilterSupport == 0)
                throw new NotSupportedException("Cloud lighting volume format is unavailable: " + format);
        }
        if (!GpuSupport.GetInternalFormatCapabilities(ImageTarget.Texture2D, SizedInternalFormat.Rgba32f).Supported)
            throw new NotSupportedException("Cloud descriptor format is unavailable.");
    }

    /// <summary>Owns each allocation immediately so failures release every previously acquired resource.</summary>
    private void Allocate(LightingVolumeDomain domain)
    {
        direct = Volume(domain, PixelInternalFormat.Rg16f); directSpare = Volume(domain, PixelInternalFormat.Rg16f);
        diffuse = Volume(domain, PixelInternalFormat.Rgba16f); diffuseSpare = Volume(domain, PixelInternalFormat.Rgba16f);
        PreviousObservation = resources.Own(DynamicTexture2D.Create(domain.Width, domain.Width, PixelInternalFormat.Rgba32f));
        HardReference = resources.Own(DynamicTexture2D.Create(domain.Width, domain.Width, PixelInternalFormat.Rgba32f));
        var empty = new float[checked(4 * domain.Width * domain.Width)];
        PreviousObservation.UploadDataImmediate(empty); HardReference.UploadDataImmediate(empty);
        regions = Buffer(domain.Regions * 16); regionSpare = Buffer(domain.Regions * 16);
        // Reserve 256 bytes for maintenance/density parameters within the fixed allowance.
        Scratch = Buffer(65280 / 4);
        GlDebug.ThrowIfErrors("Cloud lighting volume allocation");
    }

    /// <summary>Allocates continuous, clamped, single-level hardware-filterable lighting storage.</summary>
    private DynamicTexture3D Volume(LightingVolumeDomain domain, PixelInternalFormat format) => resources.Own(
        DynamicTexture3D.Create(domain.Size, LightingVolumeDomain.Height, domain.Size, format,
            textureTarget: TextureTarget.Texture3D));

    /// <summary>Initializes exact integer metadata through the existing buffer owner.</summary>
    private GpuShaderStorageBuffer Buffer(int words)
    {
        var result = resources.Own(GpuShaderStorageBuffer.Create()); result.UploadData(new uint[words]); return result;
    }
    #endregion

    #region Submission and retirement
    /// <summary>Submits disjoint writes through the engine boundary and completes all consumer visibility barriers.</summary>
    private void Run(LightingVolumeDomain target, LightingVolumeDomain? source,
        LightingVolumeEncoding from, LightingVolumeEncoding to, int mode)
    {
        program!.Capture(target, source, from, to, Generation, mode);
        program.DirectInput = direct; program.DiffuseInput = diffuse;
        program.DirectOutput = new(directSpare!, TextureAccess.WriteOnly, Layered: true);
        program.DiffuseOutput = new(diffuseSpare!, TextureAccess.WriteOnly, Layered: true);
        program.RegionInput = regions; program.RegionOutput = regionSpare;
        var bindings = program.ProgramLayout.BinaryInterface?.PreparedBindings
            ?? throw new InvalidOperationException("Lighting maintenance bindings are unavailable.");
        if (!EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration("Cloud.LightingVolume.Maintenance"),
            EngineBoundaryResources.From(bindings), _ =>
            {
                GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit |
                    MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit |
                    MemoryBarrierFlags.BufferUpdateBarrierBit | MemoryBarrierFlags.TextureUpdateBarrierBit);
                program.Dispatch((target.Size + 3) / 4, 9, (target.Size + 3) / 4);
                GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit |
                    MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit |
                    MemoryBarrierFlags.BufferUpdateBarrierBit | MemoryBarrierFlags.TextureUpdateBarrierBit);
            })) throw new InvalidOperationException("Lighting maintenance boundary rejected.");
        GlDebug.ThrowIfErrors("Cloud lighting volume maintenance");
    }

    /// <summary>Publishes node fields and region state as one complete material-domain set.</summary>
    private void Swap()
    {
        (direct, directSpare) = (directSpare, direct); (diffuse, diffuseSpare) = (diffuseSpare, diffuse);
        (regions, regionSpare) = (regionSpare, regions);
    }

    /// <summary>Clears readiness before disposing all owned fields, including partial allocations.</summary>
    private void Retire()
    {
        Ready = false; Domain = null; owner = null;
        try { resources.Dispose(); }
        finally
        {
            resources = new(); direct = directSpare = diffuse = diffuseSpare = null;
            regions = regionSpare = null; PreviousObservation = HardReference = null; Scratch = null;
        }
    }
    #endregion
    #endregion
}
