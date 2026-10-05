using HarmonyLib;

using OpenTK.Graphics.OpenGL;

using System;
using VanillaGraphicsExpanded.Rendering;
using System.Collections.Generic;
using System.Reflection;

using VanillaGraphicsExpanded.LumOn.Scene;

using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Restores Surface Cache mapping resources when vanilla terrain programs are used.</summary>
internal static class TerrainLumonSceneChunkSlotUniformBindingHook
{
    private static readonly Dictionary<int, int> genSamplerLocCache = new();

    private static readonly Dictionary<int, int> terrainBridgeBlockIndexCache = new();
    private static readonly Dictionary<int, int> slotBlockIndexCache = new();

    private static readonly Dictionary<int, int> lastAppliedVersionByProgramId = new();

    /// <summary>Installs program-use refresh independently of renderer atlas call-site binding.</summary>
    public static void ApplyPatches(Harmony harmony, Action<string> log)
    {
        // Program use also restores mapping after frame-level slot-window changes.
        try
        {
            MethodInfo? useMethod = AccessTools.Method(typeof(ShaderProgramBase), nameof(ShaderProgramBase.Use));
            if (useMethod is null)
            {
                log("[VGE] TerrainLumonSceneChunkSlotUniformBindingHook: ShaderProgramBase.Use() not found.");
            }
            else
            {
                var usePostfix = new HarmonyMethod(typeof(TerrainLumonSceneChunkSlotUniformBindingHook), nameof(Use_Postfix));
                harmony.Patch(useMethod, postfix: usePostfix);
                log("[VGE] Patched ShaderProgramBase.Use() (LumonScene chunkSlot uniforms)");
            }
        }
        catch (Exception ex)
        {
            log($"[VGE] Failed to patch ShaderProgramBase.Use() (LumonScene chunkSlot uniforms): {ex.Message}");
        }
    }

    /// <summary>Restores mapping on program use and after renderer atlas selection.</summary>
    public static void Use_Postfix(ShaderProgramBase __instance)
    {
        ApplyUniformsIfNeeded(__instance);
    }

    /// <summary>Restores both slot mapping and world-coordinate resources required by terrain feedback.</summary>
    private static void ApplyUniformsIfNeeded(ShaderProgramBase program)
    {
        // Only run for valid program ids; ignore early init/shutdown.
        int programId = program.ProgramId;
        if (programId == 0)
        {
            return;
        }

        try
        {
            int genLoc = GetUniformLocCached(genSamplerLocCache, programId, LumonSceneChunkSlotUniformState.GenerationSamplerUniform);

            int blockIndex = GetUniformBlockIndexCached(terrainBridgeBlockIndexCache, programId, LumOnTerrainBridgeUboState.BlockName);
            int slotBlockIndex = GetUniformBlockIndexCached(slotBlockIndexCache, programId, LumonSceneChunkSlotUniformState.BlockName);

            // Fast path: if this program doesn't have any of the LumOn uniforms/UBO, ignore it.
            if (genLoc < 0 && blockIndex < 0 && slotBlockIndex < 0)
            {
                return;
            }

            int version = HashCode.Combine(LumonSceneChunkSlotUniformState.Version, LumOnTerrainBridgeUboState.Version);
            bool stateChanged = !lastAppliedVersionByProgramId.TryGetValue(programId, out int last) || last != version;
            if (stateChanged)
            {
                lastAppliedVersionByProgramId[programId] = version;

                // Bind the terrain bridge UBO (if the shader declares it). The binding point is per-program.
                if (blockIndex >= 0)
                {
                    GL.UniformBlockBinding(programId, blockIndex, LumOnTerrainBridgeUboState.Binding);
                }
                if (slotBlockIndex >= 0)
                    GL.UniformBlockBinding(programId, slotBlockIndex, LumonSceneChunkSlotUniformState.Binding);

                // The sampler uniform value (texture unit) is per-program; update it when state changes.
                if (genLoc >= 0 && LumonSceneChunkSlotUniformState.GenerationTextureId != 0)
                {
                    GL.Uniform1(genLoc, LumonSceneChunkSlotUniformState.GenerationTextureUnit);
                }
            }

            // IMPORTANT:
            // Texture bindings and UBO buffer bindings are global GL state and can be clobbered by other code.
            // Re-bind them whenever this program is used (not just when state changes), otherwise the chunk shaders
            // can read stale/garbage mapping state and we can end up with "no PatchId feedback / no pages allocated".

            int texId = LumonSceneChunkSlotUniformState.GenerationTextureId;
            if (genLoc >= 0 && texId != 0)
            {
                StateCache.Current.ActiveTexture(LumonSceneChunkSlotUniformState.GenerationTextureUnit);
                StateCache.Current.BindTextureOnActiveUnit(TextureTarget.Texture2D, texId);

                // Restore to unit 0 (engine code generally assumes this).
                StateCache.Current.ActiveTexture(0);
            }

            if (blockIndex >= 0)
            {
                int bufferId = LumOnTerrainBridgeUboState.BufferId;
                if (bufferId != 0)
                {
                    StateCache.Current.BindBufferBase(BufferRangeTarget.UniformBuffer, LumOnTerrainBridgeUboState.Binding, bufferId);
                }
            }
            // Compute passes reuse the Object binding; a version check alone cannot restore it.
            if (slotBlockIndex >= 0) LumonSceneChunkSlotUniformState.BindParameters();
        }
        catch
        {
            // Swallow GL errors during early init / shutdown to avoid crashing the game.
        }
    }

    private static int GetUniformLocCached(Dictionary<int, int> cache, int programId, string uniformName)
    {
        if (cache.TryGetValue(programId, out int loc))
        {
            return loc;
        }

        loc = GL.GetUniformLocation(programId, uniformName);
        cache[programId] = loc;
        return loc;
    }

    private static int GetUniformBlockIndexCached(Dictionary<int, int> cache, int programId, string blockName)
    {
        if (cache.TryGetValue(programId, out int idx))
        {
            return idx;
        }

        // GL_INVALID_INDEX is uint.MaxValue when queried via glGetUniformBlockIndex.
        int blockIndex = -1;
        try
        {
            // OpenTK returns an int here (typically -1 when not found).
            int uidx = GL.GetUniformBlockIndex(programId, blockName);
            blockIndex = (uidx < 0) ? -1 : uidx;
        }
        catch
        {
            blockIndex = -1;
        }

        cache[programId] = blockIndex;
        return blockIndex;
    }

    /// <summary>Invalidates reflected locations and bindings when vanilla shader programs are recreated.</summary>
    public static void ClearUniformCache()
    {
        genSamplerLocCache.Clear();
        terrainBridgeBlockIndexCache.Clear();
        slotBlockIndexCache.Clear();
        lastAppliedVersionByProgramId.Clear();
    }
}
