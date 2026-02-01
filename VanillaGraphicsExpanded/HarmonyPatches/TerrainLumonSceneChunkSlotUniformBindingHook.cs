using HarmonyLib;

using OpenTK.Graphics.OpenGL;

using System;
using System.Collections.Generic;
using System.Reflection;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Numerics;

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

    private static readonly Dictionary<int, int> worldChunkOffsetLocCache = new();
    private static readonly Dictionary<int, int> worldBlockRemLocCache = new();

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

        int version = HashCode.Combine(LumonSceneChunkSlotUniformState.Version, LumonSceneWorldCoordUniformState.Version);
        if (lastAppliedVersionByProgramId.TryGetValue(programId, out int last) && last == version)
        {
            return;
        }

        lastAppliedVersionByProgramId[programId] = version;

        try
        {
            int originLoc = GetUniformLocCached(originMinLocCache, programId, LumonSceneChunkSlotUniformState.OriginMinChunkUniform);
            int dimsLoc = GetUniformLocCached(dimsLocCache, programId, LumonSceneChunkSlotUniformState.DimsUniform);
            int ringLoc = GetUniformLocCached(ringLocCache, programId, LumonSceneChunkSlotUniformState.RingUniform);
            int genLoc = GetUniformLocCached(genSamplerLocCache, programId, LumonSceneChunkSlotUniformState.GenerationSamplerUniform);
            int worldChunkLoc = GetUniformLocCached(worldChunkOffsetLocCache, programId, LumonSceneWorldCoordUniformState.WorldChunkCoordOffsetUniform);
            int worldRemLoc = GetUniformLocCached(worldBlockRemLocCache, programId, LumonSceneWorldCoordUniformState.WorldBlockOffsetRemUniform);

            var origin = LumonSceneChunkSlotUniformState.OriginMinChunk;
            var dims = LumonSceneChunkSlotUniformState.Dims;
            var ring = LumonSceneChunkSlotUniformState.Ring;

            if (originLoc >= 0) GL.Uniform3(originLoc, origin.X, origin.Y, origin.Z);
            if (dimsLoc >= 0) GL.Uniform3(dimsLoc, dims.X, dims.Y, dims.Z);
            if (ringLoc >= 0) GL.Uniform3(ringLoc, ring.X, ring.Y, ring.Z);

            VectorInt3 offChunk = LumonSceneWorldCoordUniformState.WorldChunkCoordOffset;
            Vector3d offRem = LumonSceneWorldCoordUniformState.WorldBlockOffsetRem;

            if (worldChunkLoc >= 0) GL.Uniform3(worldChunkLoc, offChunk.X, offChunk.Y, offChunk.Z);
            if (worldRemLoc >= 0) GL.Uniform3(worldRemLoc, (float)offRem.X, (float)offRem.Y, (float)offRem.Z);

            int texId = LumonSceneChunkSlotUniformState.GenerationTextureId;
            if (genLoc >= 0 && texId != 0)
            {
                GL.ActiveTexture(TextureUnit.Texture0 + LumonSceneChunkSlotUniformState.GenerationTextureUnit);
                GL.BindTexture(TextureTarget.Texture2D, texId);
                GL.Uniform1(genLoc, LumonSceneChunkSlotUniformState.GenerationTextureUnit);

                // Restore to unit 0 (engine code generally assumes this).
                GL.ActiveTexture(TextureUnit.Texture0);
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

    public static void ClearUniformCache()
    {
        originMinLocCache.Clear();
        dimsLocCache.Clear();
        ringLocCache.Clear();
        genSamplerLocCache.Clear();
        worldChunkOffsetLocCache.Clear();
        worldBlockRemLocCache.Clear();
        lastAppliedVersionByProgramId.Clear();
    }
}
