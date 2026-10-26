using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
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
    /// <summary>Binds a complete current page or neutral data; never inherits a previous page's relief.</summary>
    internal static void Bind(ShaderProgramBase program, int atlas, MaterialAtlasTextureStore store)
    {
        if (!programs.TryGetValue(program, out var linked) || linked.ProgramId != program.ProgramId) return;
        neutral ??= RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Width = 1, Height = 1, Format = RHIFormat.Rgba32f, MipLevels = 1, Storage = RHIStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = "vge_relief_neutral" }, [new RHITextureInitialData(new(new(), 0, 0, 0, 1, 1), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.Rgba32f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { 0,0,0,0 }).AsSpan()).ToArray())]).RequireResource();
        neutralHeight ??= RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Width = 1, Height = 1, Format = RHIFormat.Rgba32f, MipLevels = 1, Storage = RHIStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = "vge_relief_neutral_height" }, [new RHITextureInitialData(new(new(), 0, 0, 0, 1, 1), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.Rgba32f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { .5f,.5f,1,.5f }).AsSpan()).ToArray())]).RequireResource();
        bool available = store.TryGetDisplacementTextures(atlas, out var page)
            && store.TryGetNormalDepthTextureId(atlas, out _);
        int height = neutralHeight.TextureId;
        if (available) store.TryGetNormalDepthTextureId(atlas, out height);
        program.BindTexture2D("vge_displacementTex", available ? page!.Indices.TextureId : neutral.TextureId, IndexUnit);
        program.BindTexture2D("vge_displacementRecords", available ? page!.Records.TextureId : neutral.TextureId, RecordUnit);
        program.BindTexture2D("vge_normalDepthTex", height, HeightUnit);
        var cache = StateCache.Current;
        OpenGLImageBindings.BindSampler(cache, IndexUnit, GpuSamplers.NearestClamp.SamplerId);
        OpenGLImageBindings.BindSampler(cache, RecordUnit, GpuSamplers.NearestClamp.SamplerId);
        OpenGLImageBindings.BindSampler(cache, HeightUnit, GpuSamplers.NearestClamp.SamplerId);
    }
    #endregion
}
