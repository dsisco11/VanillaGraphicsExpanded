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
    /// <summary>Checks actual GL 4.5 limits and the planned four-bank fragment/compute sampler layout.</summary>
    private static void Validate(ShadowPageSettings settings, int availableSamplerSlots)
    {
        var caps = GpuSupport.Graphics;
        long count = checked(settings.LayersPerArray * (long)settings.ArrayCapacity);
        long metadata = count * 256 + 352;
        long total = metadata + count * settings.PageSize * settings.PageSize * 4;
        if (caps.ApiVersion is null || caps.ApiVersion < new Version(4, 5) || availableSamplerSlots < 4
            || caps.MaxTextureImageUnits < 4 || caps.MaxComputeTextureImageUnits < 4 || caps.MaxCombinedTextureImageUnits < 4
            || caps.MaxFragmentShaderStorageBlocks < 1 || caps.MaxComputeShaderStorageBlocks < 3 || caps.MaxShaderStorageBufferBindings < 3
            || settings.PageSize > caps.MaxTextureSize || settings.LayersPerArray > caps.MaxArrayTextureLayers
            || count > 8192 || metadata > caps.MaxShaderStorageBlockSize || total > settings.MemoryMiB * 1048576L)
            throw new InvalidOperationException("Page layout exceeds memory, planned consumer bindings or GL 4.5 limits.");
    }
    /// <summary>Allocates one metadata layout and starts depth growth at one resident array.</summary>
    private void Initialize(ShadowPageSettings settings)
    {
        Validate(settings, availableSamplerSlots); policy = settings; targetArrays = settings.ArrayCapacity;
        long id = Interlocked.Increment(ref incarnation);
        if (id <= 0 || id > uint.MaxValue) throw new InvalidOperationException("Page owner generations exhausted.");
        shader = ShadowPageShader.Create(api); executable = shader.Shader;
        commands = Buffer(320, "Commands"); Entries = Buffer(checked(Capacity * 256), "Entries"); Results = Buffer(32, "Results");
        resourcesAlive = true;
        Entries.UploadSubData<uint>(new uint[Capacity * 64], 0);
        Results.UploadSubData<uint>([0, 0, 0, 0, 0, (uint)id, 0, 0], 0);
        withdrawn = false;
        if (!Grow()) throw new InvalidOperationException("Initial shadow depth storage could not be allocated.");
    }
    /// <summary>Allocates fixed mutable SSBO storage through the owning RHI.</summary>
    private static GpuShaderStorageBuffer Buffer(int bytes, string name) => RHICommandContext.Current.CreateShaderStorageBuffer(
        new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = bytes,
            Usage = BufferUsage.DynamicDraw, Label = "Shadows.Pages." + name }).RequireResource();
    /// <summary>Releases only owned resources; no engine geometry is retained by page storage.</summary>
    private void ReleaseResources()
    {
        foreach (var array in arrays) array.Dispose(); arrays.Clear();
        commands?.Dispose(); Entries?.Dispose(); Results?.Dispose(); shader?.Dispose();
        resourcesAlive = false;
    }
    #endregion
}
