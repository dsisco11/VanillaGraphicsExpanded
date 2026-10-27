using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using FluffyClouds;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Observes native map work and retains non-owning metadata recipients to qualify content certificates.</summary>
internal sealed class CloudNativeMapAdapter
{
    private static readonly AccessTools.FieldRef<CloudRendererMap, int> Raw1 = AccessTools.FieldRefAccess<CloudRendererMap, int>("TextureData1");
    private static readonly AccessTools.FieldRef<CloudRendererMap, int> Raw2 = AccessTools.FieldRefAccess<CloudRendererMap, int>("TextureData2");
    private static readonly AccessTools.FieldRef<CloudRendererMap, int> Framebuffer = AccessTools.FieldRefAccess<CloudRendererMap, int>("Framebuffer");
    private static readonly AccessTools.FieldRef<CloudRendererMap, IShaderProgram> Program = AccessTools.FieldRefAccess<CloudRendererMap, IShaderProgram>("prog");
    private static readonly AccessTools.FieldRef<CloudRendererMap, float> Clock = AccessTools.FieldRefAccess<CloudRendererMap, float>("time");
    private static readonly AccessTools.FieldRef<CloudRendererMap, bool> Enabled = AccessTools.FieldRefAccess<CloudRendererMap, bool>("renderCloudMap");
    private readonly CloudRendererMap owner;
    private readonly CloudMapPublication publication;
    private CloudMapStorage storage;
    private bool generationClean;
    private IShaderProgram? validatedProgram;
    private bool shaderDirty = true;
    private bool retired;
    private BorrowedTexture[] storageTextures = [];
    private ulong[] storageRevisions = [];

    #region Public API
    #region Acquisition
    /// <summary>Validates late-acquired program/storage, but deliberately waits for the next observed upload.</summary>
    internal CloudNativeMapAdapter(CloudRendererMap owner, long world)
    {
        this.owner = owner; publication = new(owner, world);
        BeginAllocation(); CompleteAllocation();
        RefreshShader();
    }

    /// <summary>Exposes certificates for same-frame validation by the draw consumer.</summary>
    internal CloudMapPublication Publication => publication;
    #endregion

    #region Storage and program observation
    /// <summary>Invalidates before native deletion, covering same-size recreation and GL name reuse.</summary>
    internal void BeginAllocation() { publication.BeginAllocation(); }

    /// <summary>Checks actual texture dimensions and framebuffer completeness after replacement, restoring all inspected bindings.</summary>
    internal void CompleteAllocation()
    {
        if (retired) return;
        ReleaseStorageReferences();
        storage = CaptureStorage();
        bool valid;
        try { valid = ValidateStorage(); }
        catch
        {
            ReleaseStorageReferences();
            publication.CompleteAllocation(storage, false);
            throw;
        }
        publication.CompleteAllocation(storage, valid);
    }

    /// <summary>Invalidates raw content before an in-place native upload and withdraws its joint content certificate.</summary>
    internal void BeginUpload() { publication.BeginUpload(); }

    /// <summary>The native method submits both raw textures; a reported failure invalidates their joint certificate.</summary>
    internal void CompleteUpload(bool success)
    {
        generationClean &= success;
        publication.CompleteUpload(success && StorageReferencesCurrent());
    }

    /// <summary>Withdraws map publication synchronously before native shader replacement.</summary>
    internal void InvalidateShader() { publication.BeginShader(); shaderDirty = true; }
    #endregion

    #region Generation and retirement
    /// <summary>Expires the last map before native tick/generation, without advancing time or weather.</summary>
    internal void BeginGeneration()
    {
        if (retired) return;
        publication.BeginGeneration();
        generationClean = Enabled(owner);
        if (CaptureStorage() != storage || !StorageReferencesCurrent()) { BeginAllocation(); CompleteAllocation(); }
        RefreshShader();
    }

    /// <summary>Copies post-generation placement only when the selected native callback actually drew successfully.</summary>
    internal void CompleteGeneration(uint frame, bool success)
    {
        var state = owner.committedState;
        var offset = owner.offset;
        var placement = new CloudPlacement(state.CenterTilePos.X, state.CenterTilePos.Z, state.TileOffsetX, state.TileOffsetZ,
            state.WindTileOffsetX, state.WindTileOffsetZ, owner.windOffsetX, owner.windOffsetZ,
            new Vector3(offset.X, offset.Y, offset.Z), Clock(owner));
        publication.CompleteGeneration(frame, CaptureStorage(), placement,
            success && generationClean && Enabled(owner) && StorageReferencesCurrent());
    }

    /// <summary>Withdraws borrowed names at world/context teardown without issuing deletion calls.</summary>
    internal void Retire()
    {
        retired = true;
        publication.Retire();
        ReleaseStorageReferences();
    }

    /// <summary>Rejects a content certificate whose observed backing storage has changed or retired.</summary>
    internal bool TryGet(long world, uint frame, out CloudMapInput input)
    {
        if (retired) { input = default; return false; }
        if (CaptureStorage() != storage || !StorageReferencesCurrent())
        {
            publication.BeginAllocation();
            input = default;
            return false;
        }
        return publication.TryGet(owner, world, frame, out input);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Checks original associations and storage revisions without reading native descriptors.</summary>
    private bool StorageReferencesCurrent()
    {
        if (storageTextures.Length != 4) return false;
        for (int index = 0; index < storageTextures.Length; index++)
        {
            var texture = storageTextures[index];
            if (!texture.IsValid || texture.StorageGeneration != storageRevisions[index] ||
                texture.ResourceManager is { } manager && !manager.IsResourceMetadataAvailable(texture)) return false;
        }
        return true;
    }

    /// <summary>Releases managed recipients while leaving all native storage with its producer.</summary>
    private void ReleaseStorageReferences()
    {
        foreach (var texture in storageTextures) texture.Dispose();
        storageTextures = [];
        storageRevisions = [];
    }

    /// <summary>Checks the current program at acquisition, reload or reference replacement rather than hooking native shader loading.</summary>
    private void RefreshShader()
    {
        var current = Program(owner);
        if (!shaderDirty && ReferenceEquals(current, validatedProgram)) return;
        publication.BeginShader();
        validatedProgram = current; shaderDirty = false;
        publication.CompleteShader(IsProgramLinked());
    }

    /// <summary>Captures scalar allocation identity without inspecting worker-mutated tiles.</summary>
    private CloudMapStorage CaptureStorage() => new(owner.TextureMap, Raw1(owner), Raw2(owner), Framebuffer(owner), owner.CloudTileLength, owner.CloudTileSize, owner.TextureCol);

    /// <summary>Checks linked status for construction callbacks that preceded observer acquisition.</summary>
    private bool IsProgramLinked()
    {
        var program = Program(owner);
        return program is not null && GpuResourceManager.AcquireExternalFacts(GpuResourceKind.Program,
            program.ProgramId, GpuProgramMetadataReader.Instance) is { ExecutableAvailable: true };
    }

    /// <summary>Checks producer layout against acquired canonical facts without changing native selectors.</summary>
    private bool ValidateStorage()
    {
        if (!storage.IsSupported) return false;
        foreach (int id in new[] { storage.Map, storage.Color, storage.Raw1, storage.Raw2 })
        {
            var texture = new BorrowedTexture(id);
            storageTextures = [.. storageTextures, texture];
            storageRevisions = [.. storageRevisions, texture.StorageGeneration];
            var image = ((GpuTextureRef)texture).RequireImage(0);
            var expected = id == storage.Map || id == storage.Color ? TextureFormat.Rgba32f : TextureFormat.Rgba16;
            if (image.Width != storage.Width || image.Height != storage.Width || image.Format != expected) return false;
        }
        var framebuffer = GpuResourceManager.AcquireExternalFacts(GpuResourceKind.Framebuffer,
            storage.Framebuffer, GpuFramebufferMetadataReader.Instance);
        if (framebuffer is not { } facts || !facts.Attachments.TryGetValue(FramebufferSlot.Color0, out var map)
            || !facts.Attachments.TryGetValue(FramebufferSlot.Color1, out var color)
            || map.Kind != GpuResourceKind.Texture || map.Id != storage.Map || color.Kind != GpuResourceKind.Texture
            || color.Id != storage.Color || map.MipLevel != 0 || color.MipLevel != 0) return false;
        return OpenGLRHIContext.IsExternalFramebufferComplete(storage.Framebuffer);
    }

    #endregion
}
