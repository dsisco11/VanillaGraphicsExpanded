using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Owns the allowlisted relief interface and coherent material bindings for engine terrain programs.</summary>
internal static class TerrainReliefBindings
{
    internal const int IndexUnit = 12, RecordUnit = 13, HeightUnit = 14;
    private static readonly ConditionalWeakTable<ShaderProgramBase, Linked> programs = new();
    private static Texture2D? neutral;
    private static Texture2D? neutralHeight;
    /// <summary>Matches registration to the installed executable, including recycled engine owners.</summary>
    private sealed record Linked(int ProgramId);

    #region Program lifecycle
    /// <summary>Discovers the explicit relief interface only after successful engine compilation.</summary>
    internal static void Register(ShaderProgram program)
    {
        programs.Remove(program);
        if (program.AssetDomain == Constants.ModId || program.PassName is not ("chunkopaque" or "chunktopsoil" or "chunkshadowmap")) return;
        if (program.HasUniform("vge_displacementTex") && program.HasUniform("vge_displacementRecords")
            && program.HasUniform("vge_normalDepthTex")) programs.Add(program, new(program.ProgramId));
    }

    /// <summary>Retires neutral resources at the existing shader/world resource boundary.</summary>
    internal static void Reset()
    {
        neutral?.Dispose();
        neutral = null;
        neutralHeight?.Dispose();
        neutralHeight = null;
    }
    #endregion

    #region Per-page binding
    /// <summary>Prepares a complete current page or neutral data for the caller's single resource submission.</summary>
    internal static int Prepare(ShaderProgramBase program, int atlas, MaterialAtlasTextureStore store, Span<RHISampledTextureSlot> slots)
    {
        if (!programs.TryGetValue(program, out var linked) || linked.ProgramId != program.ProgramId) return 0;
        if (slots.Length < 3) throw new ArgumentException("Relief requires three sampled slots.", nameof(slots));
        neutral ??= RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 1, Height = 1, Format = TextureFormat.Rgba32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = "vge_relief_neutral" }, [new RHITextureInitialData(new(new(), 0, 0, 0, 1, 1), new RHIPixelEncoding(PixelChannelLayout.Rgba, PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { 0,0,0,0 }).AsSpan()).ToArray())]).RequireResource();
        neutralHeight ??= RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 1, Height = 1, Format = TextureFormat.Rgba32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = "vge_relief_neutral_height" }, [new RHITextureInitialData(new(new(), 0, 0, 0, 1, 1), new RHIPixelEncoding(PixelChannelLayout.Rgba, PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { .5f,.5f,1,.5f }).AsSpan()).ToArray())]).RequireResource();
        bool available = store.TryGetDisplacementTextures(atlas, out var page)
            && store.TryGetNormalDepthTextureId(atlas, out _);
        GpuTextureRef height = available && store.TryGetPageTextures(atlas, out var textures) ? textures.NormalDepthTexture : neutralHeight;
        program.Uniform("vge_displacementTex", IndexUnit);
        program.Uniform("vge_displacementRecords", RecordUnit);
        program.Uniform("vge_normalDepthTex", HeightUnit);
        slots[0] = new(IndexUnit, available ? page!.Indices : neutral, GpuSamplers.NearestClamp);
        slots[1] = new(RecordUnit, available ? page!.Records : neutral, GpuSamplers.NearestClamp);
        slots[2] = new(HeightUnit, height, GpuSamplers.NearestClamp);
        return 3;
    }
    #endregion
}
