using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.Rendering;
using OpenTK.Graphics.OpenGL;

using System;
using System.Collections.Generic;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.ModSystems;

using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Binds per-atlas material and normal resources at terrain renderer call sites.</summary>
internal static class TerrainMaterialParamsTextureBindingHook
{
    private const int NormalDepthTextureUnit = 14;
    private const int MaterialTextureUnit = 15;

    /// <summary>
    /// Uniform name for the material params sampler we inject into patched shaders.
    /// </summary>
    private const string MaterialSamplerUniform = "vge_materialParamsTex";

    /// <summary>
    /// Uniform name for the normal+depth sampler we inject into patched chunk shaders.
    /// </summary>
    private const string NormalDepthSamplerUniform = "vge_normalDepthTex";

    /// <summary>Provides local complete-set storage without per-use heap allocation.</summary>
    [InlineArray(4)] private struct TextureSlots { private RHISampledTextureSlot first; }

    private static readonly Dictionary<int, int> uniformLocationCache = new();

    /// <summary>
    /// Cached uniform location per shader program id for the normal+depth sampler.
    /// </summary>
    private static readonly Dictionary<int, int> normalDepthUniformLocationCache = new();


    private static GpuTextureRef lastBoundNormalDepth;
    private static int lastBoundAtlasTexId;

    /// <summary>Reports the last atlas pair for existing material debug overlays.</summary>
    public static bool TryGetLastBoundNormalDepthTexture(out GpuTextureRef texture, out int baseAtlasTextureId)
    {
        texture = lastBoundNormalDepth;
        baseAtlasTextureId = lastBoundAtlasTexId;
        return texture.IsValid;
    }

    /// <summary>Binds material and relief resources for the atlas selected by an engine terrain draw.</summary>
    internal static void BindAtlas(ShaderProgramBase __instance, int value)
    {
        TextureSlots slots = default;
        int slotCount = TerrainReliefBindings.Prepare(__instance, value, MaterialAtlasSystem.Instance.TextureStore, slots);
        try
        {
            // Native program assignments are separate from the single validated resource publication.
            int programId = __instance.ProgramId;
            var system = MaterialAtlasSystem.Instance;
            if (value == 0 || programId == 0 || !system.IsInitialized
                || !system.TextureStore.TryGetPageTextures(value, out var selectedPage))
            {
                RHICommandContext.Current.BindSampledTextures(((ReadOnlySpan<RHISampledTextureSlot>)slots)[..slotCount]);
                return;
            }
            bool hasMaterialParams = selectedPage.MaterialParamsTexture.IsValid;
            bool hasNormalDepth = selectedPage.NormalDepthTexture?.IsValid == true;
            if (hasMaterialParams)
            {
                if (!uniformLocationCache.TryGetValue(programId, out int materialUniformLoc))
                {
                    materialUniformLoc = GL.GetUniformLocation(programId, MaterialSamplerUniform);
                    uniformLocationCache[programId] = materialUniformLoc;
                }

                if (materialUniformLoc >= 0)
                {
                    slots[slotCount++] = new(MaterialTextureUnit, selectedPage.MaterialParamsTexture);
                    OpenGLProgramAssignments.SetUnit(programId, materialUniformLoc, MaterialTextureUnit);
                }
            }

            if (hasNormalDepth)
            {
                // Capture for debug overlays (e.g., showing the currently active atlas page).
                lastBoundNormalDepth = selectedPage.NormalDepthTexture;
                lastBoundAtlasTexId = value;

                if (!normalDepthUniformLocationCache.TryGetValue(programId, out int normalDepthUniformLoc))
                {
                    normalDepthUniformLoc = GL.GetUniformLocation(programId, NormalDepthSamplerUniform);
                    normalDepthUniformLocationCache[programId] = normalDepthUniformLoc;
                }

                if (normalDepthUniformLoc >= 0)
                {
                    // Relief and the atlas share this slot; retain its explicit nearest sampler.
                    if (slotCount >= 3 && slots[2].Slot == NormalDepthTextureUnit)
                        slots[2] = slots[2] with { Texture = selectedPage.NormalDepthTexture };
                    else slots[slotCount++] = new(NormalDepthTextureUnit, selectedPage.NormalDepthTexture);
                    OpenGLProgramAssignments.SetUnit(programId, normalDepthUniformLoc, NormalDepthTextureUnit);
                }
            }
            RHICommandContext.Current.BindSampledTextures(((ReadOnlySpan<RHISampledTextureSlot>)slots)[..slotCount]);
        }
        catch
        {
            // Swallow GL errors during early init / shutdown to avoid crashing the game.
        }
    }

    /// <summary>
    /// Call when shaders are reloaded to clear cached uniform locations.
    /// </summary>
    public static void ClearUniformCache()
    {
        TerrainReliefBindings.Reset();
        uniformLocationCache.Clear();
        normalDepthUniformLocationCache.Clear();
    }
}
