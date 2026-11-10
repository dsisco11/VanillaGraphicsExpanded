using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.Client.NoObf;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Observes native world ownership and chunk range lifetime, without hooks into mesh pools.</summary>
internal static class NativeShadowWorldHooks
{
    private static NativeShadowWorldAdapter? adapter;
    private static ClientWorldMap? map;
    private static NativeShadowTessellationStamps? tessellations;
    [ThreadStatic] private static long? dirtyEventIndex;
    private static readonly AccessTools.FieldRef<ClientWorldMap, Dictionary<long, ClientChunk>> ReadChunks =
        AccessTools.FieldRefAccess<ClientWorldMap, Dictionary<long, ClientChunk>>("chunks");

    #region Public API
    /// <summary>Installs checked lifecycle seams before bootstrap; failure leaves no partial hook ownership.</summary>
    internal static void Install(Harmony harmony, ClientWorldMap world, NativeShadowWorldAdapter owner)
    {
        if (typeof(ClientMain).Assembly.ManifestModule.ModuleVersionId != new Guid("8a3ad3aa-2940-4c39-ae1b-881de9e65346"))
            throw new NotSupportedException("Native shadow publication requires the qualified Vintage Story 1.22.7 binary.");
        map = world; adapter = owner;
        tessellations = new(owner.ChangeCapacity);
        foreach (string name in new[] { "loadChunkMT", "OverloadChunkMT" })
            PatchMutation(harmony, AccessTools.Method(typeof(ClientWorldMap), name), true);
        PatchMutation(harmony, AccessTools.Method(typeof(SystemUnloadChunks), "HandleChunkUnload"), true);
        harmony.Patch(AccessTools.Method(typeof(ClientWorldMap), "SetChunkDirty"),
            prefix: new HarmonyMethod(typeof(NativeShadowWorldHooks), nameof(BeginSetDirty)),
            finalizer: new HarmonyMethod(typeof(NativeShadowWorldHooks), nameof(End)));
        harmony.Patch(AccessTools.Method(typeof(ClientWorldMap), "MarkChunkDirty"),
            prefix: new HarmonyMethod(typeof(NativeShadowWorldHooks), nameof(BeginMarkDirty)),
            finalizer: new HarmonyMethod(typeof(NativeShadowWorldHooks), nameof(EndMarkDirty)));
        harmony.Patch(AccessTools.Method(typeof(ChunkRenderer), "AddTesselatedChunk"),
            prefix: new HarmonyMethod(typeof(NativeShadowWorldHooks), nameof(BeginUpload)),
            finalizer: new HarmonyMethod(typeof(NativeShadowWorldHooks), nameof(EndUpload)));
        harmony.Patch(AccessTools.Method(typeof(ChunkTesselator), "NowProcessChunk"),
            prefix: new HarmonyMethod(typeof(NativeShadowWorldHooks), nameof(Tessellate)));
        harmony.Patch(AccessTools.Method(typeof(TesselatedChunk), "MergeIfEqual"),
            postfix: new HarmonyMethod(typeof(NativeShadowWorldHooks), nameof(Merged)));
        PatchMutation(harmony, AccessTools.Method(typeof(ClientChunk), "RemoveCenterDataPoolLocations"), false);
        PatchMutation(harmony, AccessTools.Method(typeof(ClientChunk), "RemoveEdgeDataPoolLocations"), false);
        harmony.Patch(AccessTools.Method(typeof(ClientWorldMap), "OnMapSizeReceived"),
            prefix: new HarmonyMethod(typeof(NativeShadowWorldHooks), nameof(Begin)),
            finalizer: new HarmonyMethod(typeof(NativeShadowWorldHooks), nameof(Reset)));
    }

    /// <summary>Withdraws callbacks already present in native dispatch before unpatching this owner.</summary>
    internal static void Detach() { adapter = null; map = null; tessellations = null; }

    /// <summary>Applies a new queued-work allowance without losing revision evidence for outstanding meshes.</summary>
    internal static void Reconfigure(int allowance) => tessellations?.Reconfigure(allowance);

    /// <summary>Recognizes the synchronous event already covered with native center/edge specificity.</summary>
    internal static bool CoversDirtyEvent(long index) => dirtyEventIndex == index;
    /// <summary>Observes actual dictionary writes under the engine lock instead of reconstructing loaded membership.</summary>
    internal static IEnumerable<CodeInstruction> ObserveDictionary(IEnumerable<CodeInstruction> instructions)
    {
        var setter = AccessTools.PropertySetter(typeof(Dictionary<long, ClientChunk>), "Item");
        var remove = AccessTools.Method(typeof(Dictionary<long, ClientChunk>), nameof(Dictionary<long, ClientChunk>.Remove), [typeof(long)]);
        int matches = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(setter) || instruction.Calls(remove))
            {
                bool set = instruction.Calls(setter);
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(NativeShadowWorldHooks), set ? nameof(Set) : nameof(Remove));
                matches++;
            }
            yield return instruction;
        }
        if (matches == 0) throw new InvalidOperationException("Supported native chunk ownership boundary no longer contains dictionary mutations.");
    }

    #endregion

    #region Private
    #region Native mutation intervals
    /// <summary>Pairs pre-mutation withdrawal with an exception-safe completion boundary.</summary>
    private static void PatchMutation(Harmony harmony, MethodInfo method, bool dictionary)
    {
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(NativeShadowWorldHooks), nameof(Begin)),
            finalizer: new HarmonyMethod(typeof(NativeShadowWorldHooks), nameof(End)),
            transpiler: dictionary ? new HarmonyMethod(typeof(NativeShadowWorldHooks), nameof(ObserveDictionary)) : null);
    }

    /// <summary>Captures the owner for balanced completion even if teardown occurs within native work.</summary>
    private static void Begin(out NativeShadowWorldAdapter? __state)
    {
        __state = adapter;
        __state?.BeginMutation();
    }

    /// <summary>Never suppresses a native failure or certifies the failed generation.</summary>
    private static Exception? End(Exception? __exception, NativeShadowWorldAdapter? __state)
    {
        __state?.EndMutation(__exception != null);
        return __exception;
    }

    /// <summary>Replaces bootstrap ownership after the native dictionary itself is reset.</summary>
    private static Exception? Reset(Exception? __exception, NativeShadowWorldAdapter? __state)
    {
        __state?.Reset();
        __state?.EndMutation(__exception != null);
        return __exception;
    }

    /// <summary>Tracks dirty-queue work even where the native path does not raise ChunkDirty.</summary>
    private static void BeginSetDirty(ClientWorldMap __instance, long __0, bool __3, out NativeShadowWorldAdapter? __state)
    {
        __state = ReferenceEquals(map, __instance) ? adapter : null;
        __state?.BeginMutation();
        __state?.Dirty(__0, __3);
    }

    /// <summary>Captures the actual dirty part mask before native work or its synchronous event runs.</summary>
    private static void BeginMarkDirty(ClientWorldMap __instance, int __0, int __1, int __2, bool __7,
        out NativeShadowDirtyScope __state)
    {
        var owner = ReferenceEquals(map, __instance) ? adapter : null;
        __state = new(owner, dirtyEventIndex);
        if (owner == null) return;
        owner.BeginMutation();
        long index = __instance.ChunkIndex3D(__0, __1, __2);
        dirtyEventIndex = index;
        owner.Dirty(index, __7);
    }

    /// <summary>Restores nested event ownership and always closes the native mutation interval.</summary>
    private static Exception? EndMarkDirty(Exception? __exception, NativeShadowDirtyScope __state)
    {
        dirtyEventIndex = __state.Previous;
        __state.Owner?.EndMutation(__exception != null);
        return __exception;
    }

    #endregion
    #region Tessellation and upload
    /// <summary>Captures native chunk coordinates and the edit revision before temporary upload inputs are released.</summary>
    private static void BeginUpload(TesselatedChunk __0, ClientChunk __1, out NativeShadowUpload? __state)
    {
        var owner = adapter;
        __state = null;
        if (owner == null || map == null) return;
        long index = map.ChunkIndex3D(NativeShadowTessellationFields.X(__0) / 32,
            NativeShadowTessellationFields.Y(__0) / 32, NativeShadowTessellationFields.Z(__0) / 32);
        owner.BeginMutation();
        NativeShadowUpload? stamp = null;
        bool known = tessellations != null && tessellations.TryTake(__0, out stamp) && stamp != null && ReferenceEquals(stamp.Owner, owner)
            && stamp.Index == index && ReferenceEquals(stamp.Chunk, __1);
        // Publish balanced state before metadata inspection can fail; Harmony's finalizer then
        // withdraws the result and releases the mutation interval on every exceptional path.
        __state = new(owner, index, known ? stamp!.CenterRevision : -1, known ? stamp!.EdgeRevision : -1, __1);
        __state = __state with { CenterCount = CountRanges(NativeShadowTessellationFields.Centers(__0), owner.RangeCapacity),
            EdgeCount = CountRanges(NativeShadowTessellationFields.Edges(__0), owner.RangeCapacity) };
    }

    /// <summary>Stamps the queued native mesh before its voxel reads; intervening edits cannot be certified by upload.</summary>
    private static void Tessellate(int __0, int __1, int __2, TesselatedChunk __3, bool __4)
    {
        var owner = adapter;
        if (owner == null || map == null) return;
        long index = map.ChunkIndex3D(__0, __1, __2);
        var revisions = owner.ContentRevisions(index);
        tessellations?.TryAdd(__3, new(owner, index, __4 ? -1 : revisions.Center, revisions.Edge,
            NativeShadowTessellationFields.Chunk(__3)));
    }

    /// <summary>Transfers provenance when the native normal queue merges incoming parts into an older mesh.</summary>
    private static void Merged(TesselatedChunk __instance, TesselatedChunk __0, bool __result)
    {
        if (__result) tessellations?.Merge(__instance, __0);
    }

    /// <summary>Completes an upload without hiding exceptions or accepting an intervening edit.</summary>
    private static Exception? EndUpload(Exception? __exception, NativeShadowUpload? __state)
    {
        if (__state is { } state)
        {
            bool failed = __exception != null || state.CenterCount == -2 || state.EdgeCount == -2
                || (state.CenterCount >= 0 && (state.Chunk == null || NativeShadowChunkFields.Centers(state.Chunk)?.Length != state.CenterCount))
                || (state.EdgeCount >= 0 && (state.Chunk == null || NativeShadowChunkFields.Edges(state.Chunk)?.Length != state.EdgeCount));
            state.Owner.UploadFinished(state.Index, state.CenterRevision, state.EdgeRevision, failed, state.Chunk,
                state.CenterCount != -1, state.EdgeCount != -1);
        }
        return __exception;
    }

    /// <summary>Detects native AddModel returning no location for nonempty mesh data, without observing pools.</summary>
    private static int CountRanges(TesselatedChunkPart[]? parts, int allowance)
    {
        if (parts == null) return -1;
        if (parts.Length > allowance) return -2;
        int count = 0;
        foreach (var part in parts)
        {
            if (part == null) return -2;
            if (NativeShadowTessellationFields.Lod0(part) != null) count++;
            if (NativeShadowTessellationFields.Lod1(part) != null) count++;
            if (NativeShadowTessellationFields.Near2(part) != null) count++;
            if (NativeShadowTessellationFields.Far2(part) != null) count++;
            if (count > allowance) return -2;
        }
        return count;
    }

    #endregion
    #region Native dictionary notifications
    /// <summary>Publishes the native key immediately after a load/replacement under the native lock.</summary>
    private static void Set(Dictionary<long, ClientChunk> chunks, long index, ClientChunk chunk)
    {
        chunks[index] = chunk;
        if (map != null && ReferenceEquals(chunks, ReadChunks(map))) adapter?.Changed(index);
    }

    /// <summary>Publishes native removal without reading an array of all loaded chunk indices.</summary>
    private static bool Remove(Dictionary<long, ClientChunk> chunks, long index)
    {
        bool removed = chunks.Remove(index);
        if (removed && map != null && ReferenceEquals(chunks, ReadChunks(map))) adapter?.Changed(index);
        return removed;
    }
    #endregion
    #endregion
}
