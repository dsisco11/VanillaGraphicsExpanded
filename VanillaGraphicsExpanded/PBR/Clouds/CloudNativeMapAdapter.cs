using System.Numerics;
using FluffyClouds;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
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
    internal void BeginAllocation() { publication.BeginAllocation(); DrainErrors(); }

    /// <summary>Checks actual texture dimensions and framebuffer completeness after replacement, restoring all inspected bindings.</summary>
    internal void CompleteAllocation()
    {
        if (retired) return;
        ReleaseStorageReferences();
        storage = CaptureStorage();
        bool clean = DrainErrors();
        bool valid = ValidateStorage();
        bool validationClean = DrainErrors();
        if (clean && valid && validationClean)
        {
            try
            {
                // These are ordinary non-owning recipients. The manager remains the sole
                // lifetime authority; revisions only qualify this producer's content certificate.
                foreach (int id in new[] { storage.Map, storage.Color, storage.Raw1, storage.Raw2 })
                {
                    var texture = new BorrowedTexture(id);
                    storageTextures = [.. storageTextures, texture];
                    storageRevisions = [.. storageRevisions, texture.StorageGeneration];
                }
            }
            catch
            {
                ReleaseStorageReferences();
                publication.CompleteAllocation(storage, false);
                throw;
            }
        }
        publication.CompleteAllocation(storage, clean && valid && validationClean);
    }

    /// <summary>Invalidates raw content before an in-place native upload and isolates pre-existing GL errors.</summary>
    internal void BeginUpload() { publication.BeginUpload(); generationClean &= DrainErrors(); }

    /// <summary>The native method submits both raw textures; any error invalidates their joint certificate.</summary>
    internal void CompleteUpload(bool success)
    {
        bool clean = DrainErrors(); generationClean &= clean && success;
        publication.CompleteUpload(success && clean && StorageReferencesCurrent());
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
        generationClean = DrainErrors() && Enabled(owner);
        if (CaptureStorage() != storage || !StorageReferencesCurrent()) { BeginAllocation(); CompleteAllocation(); }
        RefreshShader();
    }

    /// <summary>Copies post-generation placement only when the selected native callback actually drew successfully.</summary>
    internal void CompleteGeneration(uint frame, bool success)
    {
        bool clean = DrainErrors();
        var state = owner.committedState;
        var offset = owner.offset;
        var placement = new CloudPlacement(state.CenterTilePos.X, state.CenterTilePos.Z, state.TileOffsetX, state.TileOffsetZ,
            state.WindTileOffsetX, state.WindTileOffsetZ, owner.windOffsetX, owner.windOffsetZ,
            new Vector3(offset.X, offset.Y, offset.Z), Clock(owner));
        publication.CompleteGeneration(frame, CaptureStorage(), placement,
            success && clean && generationClean && Enabled(owner) && StorageReferencesCurrent());
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
        if (program is null || program.ProgramId <= 0 || !GL.IsProgram(program.ProgramId)) return false;
        GL.GetProgram(program.ProgramId, GetProgramParameterName.LinkStatus, out int linked);
        return linked != 0 && DrainErrors();
    }

    /// <summary>Validates native storage once per allocation, preserving distinct read/draw framebuffer and texture bindings.</summary>
    private bool ValidateStorage()
    {
        if (!storage.IsSupported || !GL.IsFramebuffer(storage.Framebuffer)) return false;
        GL.GetInteger(GetPName.DrawFramebufferBinding, out int draw);
        GL.GetInteger(GetPName.ReadFramebufferBinding, out int read);
        GL.GetInteger(GetPName.TextureBinding2D, out int texture);
        try
        {
            foreach (int id in new[] { storage.Map, storage.Color, storage.Raw1, storage.Raw2 })
            {
                if (!GL.IsTexture(id)) return false;
                GL.BindTexture(TextureTarget.Texture2D, id);
                GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureWidth, out int width);
                GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureHeight, out int height);
                GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureInternalFormat, out int format);
                if (width != storage.Width || height != storage.Width) return false;
                var expected = id == storage.Map || id == storage.Color ? PixelInternalFormat.Rgba32f : PixelInternalFormat.Rgba16;
                if (format != (int)expected) return false;
            }
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, storage.Framebuffer);
            GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                FramebufferParameterName.FramebufferAttachmentObjectName, out int map);
            GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1,
                FramebufferParameterName.FramebufferAttachmentObjectName, out int color);
            if (map != storage.Map || color != storage.Color) return false;
            return GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == FramebufferErrorCode.FramebufferComplete;
        }
        finally
        {
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, draw);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read);
        }
    }

    /// <summary>Separates error boundaries without synchronizing GPU execution or reading texture data.</summary>
    private static bool DrainErrors()
    {
        bool clean = true;
        while (GL.GetError() != ErrorCode.NoError) clean = false;
        return clean;
    }
    #endregion
}
