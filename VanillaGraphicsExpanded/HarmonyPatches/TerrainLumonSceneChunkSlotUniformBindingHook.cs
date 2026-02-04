using HarmonyLib;

using OpenTK.Graphics.OpenGL;

using System;
using System.Collections.Generic;
using System.Reflection;

using VanillaGraphicsExpanded.LumOn.Scene;

using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

internal static class TerrainLumonSceneChunkSlotUniformBindingHook
{
    private static readonly (string TypeName, string PropertyName)[] TargetProperties =
    {
        ("Vintagestory.Client.NoObf.ShaderProgramChunkopaque", "TerrainTex2D"),
        ("Vintagestory.Client.NoObf.ShaderProgramChunkopaque", "TerrainTex2DLinear"),
        ("Vintagestory.Client.NoObf.ShaderProgramChunktopsoil", "TerrainTex2D"),
        ("Vintagestory.Client.NoObf.ShaderProgramChunktopsoil", "TerrainTex2DLinear"),
        ("Vintagestory.Client.NoObf.ShaderProgramChunkliquid", "TerrainTex2D"),
        ("Vintagestory.Client.NoObf.ShaderProgramChunktransparent", "TerrainTex2D"),
    };

    private static readonly Dictionary<int, int> originMinLocCache = new();
    private static readonly Dictionary<int, int> dimsLocCache = new();
    private static readonly Dictionary<int, int> ringLocCache = new();
    private static readonly Dictionary<int, int> genSamplerLocCache = new();

    private static readonly Dictionary<int, int> terrainBridgeBlockIndexCache = new();

    private static readonly Dictionary<int, int> lastAppliedVersionByProgramId = new();

    public static void ApplyPatches(Harmony harmony, Action<string> log)
    {
        var postfix = new HarmonyMethod(typeof(TerrainLumonSceneChunkSlotUniformBindingHook), nameof(SetTex2dTerrain_Postfix));
        int patchedCount = 0;

        foreach ((string typeName, string propertyName) in TargetProperties)
        {
            Type? type = AccessTools.TypeByName(typeName);
            if (type is null)
            {
                log($"[VGE] TerrainLumonSceneChunkSlotUniformBindingHook: type not found: {typeName}");
                continue;
            }

            MethodInfo? setter = AccessTools.PropertySetter(type, propertyName);
            if (setter is null)
            {
                log($"[VGE] TerrainLumonSceneChunkSlotUniformBindingHook: property setter not found: {typeName}.{propertyName}");
                continue;
            }

            try
            {
                harmony.Patch(setter, postfix: postfix);
                patchedCount++;
                log($"[VGE] Patched {typeName}.set_{propertyName} (LumonScene chunkSlot uniforms)");
            }
            catch (Exception ex)
            {
                log($"[VGE] Failed to patch {typeName}.set_{propertyName} (LumonScene chunkSlot uniforms): {ex.Message}");
            }
        }

        log($"[VGE] TerrainLumonSceneChunkSlotUniformBindingHook: {patchedCount}/{TargetProperties.Length} property setters patched.");

        // Also patch ShaderProgramBase.Use() as a reliable fallback (property setters are not guaranteed to run
        // after we update the slot window each frame). We keep this light by caching per-program version.
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

    public static void SetTex2dTerrain_Postfix(ShaderProgramBase __instance, int value)
    {
        _ = value;
        ApplyUniformsIfNeeded(__instance);
    }

    public static void Use_Postfix(ShaderProgramBase __instance)
    {
        ApplyUniformsIfNeeded(__instance);
    }

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
            int originLoc = GetUniformLocCached(originMinLocCache, programId, LumonSceneChunkSlotUniformState.OriginMinChunkUniform);
            int dimsLoc = GetUniformLocCached(dimsLocCache, programId, LumonSceneChunkSlotUniformState.DimsUniform);
            int ringLoc = GetUniformLocCached(ringLocCache, programId, LumonSceneChunkSlotUniformState.RingUniform);
            int genLoc = GetUniformLocCached(genSamplerLocCache, programId, LumonSceneChunkSlotUniformState.GenerationSamplerUniform);

            int blockIndex = GetUniformBlockIndexCached(terrainBridgeBlockIndexCache, programId, LumOnTerrainBridgeUboState.BlockName);

            // Fast path: if this program doesn't have any of the LumOn uniforms/UBO, ignore it.
            if (originLoc < 0 && dimsLoc < 0 && ringLoc < 0 && genLoc < 0 && blockIndex < 0)
            {
                return;
            }

            int version = HashCode.Combine(LumonSceneChunkSlotUniformState.Version, LumOnTerrainBridgeUboState.Version);
            bool stateChanged = !lastAppliedVersionByProgramId.TryGetValue(programId, out int last) || last != version;
            if (stateChanged)
            {
                lastAppliedVersionByProgramId[programId] = version;

                var origin = LumonSceneChunkSlotUniformState.OriginMinChunk;
                var dims = LumonSceneChunkSlotUniformState.Dims;
                var ring = LumonSceneChunkSlotUniformState.Ring;

                if (originLoc >= 0) GL.Uniform3(originLoc, origin.X, origin.Y, origin.Z);
                if (dimsLoc >= 0) GL.Uniform3(dimsLoc, dims.X, dims.Y, dims.Z);
                if (ringLoc >= 0) GL.Uniform3(ringLoc, ring.X, ring.Y, ring.Z);

                // Bind the terrain bridge UBO (if the shader declares it). The binding point is per-program.
                if (blockIndex >= 0)
                {
                    GL.UniformBlockBinding(programId, blockIndex, LumOnTerrainBridgeUboState.Binding);
                }

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
                GL.ActiveTexture(TextureUnit.Texture0 + LumonSceneChunkSlotUniformState.GenerationTextureUnit);
                GL.BindTexture(TextureTarget.Texture2D, texId);

                // Restore to unit 0 (engine code generally assumes this).
                GL.ActiveTexture(TextureUnit.Texture0);
            }

            if (blockIndex >= 0)
            {
                int bufferId = LumOnTerrainBridgeUboState.BufferId;
                if (bufferId != 0)
                {
                    GL.BindBufferBase(BufferRangeTarget.UniformBuffer, LumOnTerrainBridgeUboState.Binding, bufferId);
                }
            }
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

    public static void ClearUniformCache()
    {
        originMinLocCache.Clear();
        dimsLocCache.Clear();
        ringLocCache.Clear();
        genSamplerLocCache.Clear();
        terrainBridgeBlockIndexCache.Clear();
        lastAppliedVersionByProgramId.Clear();
    }
}
