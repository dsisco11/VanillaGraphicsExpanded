using System;
using System.Threading;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using Vintagestory.API.Common;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Pages;

/// <summary>Validates and creates the single charged storage layout for a page pool.</summary>
internal sealed partial class ShadowPagePool
{
    private static long incarnation;
    #region Private
    /// <summary>Captures context affinity and cleans up partial allocation failure.</summary>
    private ShadowPagePool(ICoreAPI api, ShadowPageSettings settings, int availableSamplerSlots)
    {
        this.api = api; this.availableSamplerSlots = availableSamplerSlots;
        context = Integration.RenderContextRegistry.Current();
        try
        {
            Initialize(settings);
            if (api is ICoreClientAPI client)
                reloadRegistration = Integration.ClientRenderingLifetime.Find(client)?.RegisterPublication(Withdraw);
            Integration.RenderContextRegistry.Retiring += OnContextRetiring;
        }
        catch { ReleaseResources(); throw; }
    }
    /// <summary>Checks actual GL 4.5 array, image, sampler and storage limits for the complete layout.</summary>
    private static void Validate(ShadowPageSettings settings, int availableSamplerSlots)
    {
        var caps = GpuSupport.Graphics;
        long count = checked((settings.TileColumns * settings.TileRows) * (long)settings.ArrayLayers);
        long metadata = count * 528 + 608;
        long total = metadata + count * settings.PageSize * settings.PageSize * 4;
        if (caps.ApiVersion is null || caps.ApiVersion < new Version(4, 5) || availableSamplerSlots < 1
            || caps.MaxTextureImageUnits < 1 || caps.MaxComputeTextureImageUnits < 1 || caps.MaxCombinedTextureImageUnits < 1
            || caps.MaxFragmentShaderStorageBlocks < 2 || caps.MaxComputeShaderStorageBlocks < 4 || caps.MaxShaderStorageBufferBindings < 4
            || checked(settings.PageSize * settings.TileColumns) > caps.MaxTextureSize
            || checked(settings.PageSize * settings.TileRows) > caps.MaxTextureSize
            || caps.MaxComputeImageUniforms < 1 || caps.MaxFragmentImageUniforms < 1
            || caps.MaxImageUnits < 1 || caps.MaxCombinedImageUnits < 1
            || settings.ArrayLayers > caps.MaxArrayTextureLayers
            || count > 8192 || metadata > caps.MaxShaderStorageBlockSize || total > settings.MemoryMiB * 1048576L)
            throw new InvalidOperationException("Page layout exceeds memory, planned consumer bindings or GL 4.5 limits.");
    }
    /// <summary>Allocates the entire validated texture-array capacity and metadata as one ownership unit.</summary>
    private void Initialize(ShadowPageSettings settings)
    {
        Validate(settings, availableSamplerSlots); policy = settings;
        long id = Interlocked.Increment(ref incarnation);
        if (id <= 0 || id > uint.MaxValue) throw new InvalidOperationException("Page owner generations exhausted.");
        shader = ShadowPageShader.Create(api); executable = shader.Shader;
        clearShader = ShadowTileClearShader.Create(api); clearExecutable = clearShader.Shader;
        commands = Buffer(576, "Commands"); Entries = Buffer(checked(Capacity * 512), "Entries"); Results = Buffer(32, "Results");
        WritableAssignments = Buffer(checked(Capacity * 16), "Writable");
        resourcesAlive = true;
        Entries.UploadSubData<uint>(new uint[Capacity * 128], 0);
        WritableAssignments.UploadSubData<uint>(new uint[Capacity * 4], 0);
        Results.UploadSubData<uint>([0, 0, 0, 0, 0, (uint)id, 0, 0], 0);
        withdrawn = false;
        depthArray = RHICommandContext.Current.CreateTexture(new RHITextureDescription {
            Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui,
            Width = checked(policy.PageSize * policy.TileColumns), Height = checked(policy.PageSize * policy.TileRows),
            Depth = policy.ArrayLayers, MipLevels = 1, Storage = ResourceStorageMode.Immutable,
            Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = 0,
            Label = "Shadows.DepthArray" }).RequireResource();
    }
    /// <summary>Allocates fixed mutable SSBO storage through the owning RHI.</summary>
    private static GpuShaderStorageBuffer Buffer(int bytes, string name) => RHICommandContext.Current.CreateShaderStorageBuffer(
        new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = bytes,
            Usage = BufferUsage.DynamicDraw, Label = "Shadows.Pages." + name }).RequireResource();
    /// <summary>Releases only owned resources; no engine geometry is retained by page storage.</summary>
    private void ReleaseResources()
    {
        depthArray?.Dispose(); depthArray = null;
        commands?.Dispose(); Entries?.Dispose(); Results?.Dispose(); WritableAssignments?.Dispose(); clearShader?.Dispose(); shader?.Dispose();
        resourcesAlive = false;
    }
    #endregion
}
