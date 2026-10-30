using System;
using OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes owner-authorized storage and sampling policy through complete semantic operations.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void ConfigureTexture(GpuTexture owner, in RHISamplerDescription description) => ApplyTextureSampling(owner, description);

    /// <summary>Replaces mutable storage in place while retaining untouched higher images.</summary>
    public void AllocateTextureStorage(GpuTexture owner, in RHITextureDescription description)
    {
        var request = description;
        RequireContext();
        owner.RequireOriginalContext();
        owner.RequireMutableStorage();
        var initialization = LowerTexture(request).Resolve();
        var previous = RHIResourceManager.AcquireTextureCommandInput(owner);
        if (previous.ImmutableStorage || previous.Target != initialization.Target)
            throw new InvalidOperationException("In-place allocation requires mutable storage of the established shape.");
        using var operation = new RHIResourceManager.MetadataOperation<GpuTextureMetadata>(owner, GpuMetadataChange.Storage);
        var candidate = DefineTextureImages(owner.TextureId, initialization, operation);
        var images = candidate.Levels.ToBuilder();
        foreach (var image in previous.Levels)
            if (image.Level >= initialization.Levels) images.Add(image);
        ApplyTextureFilterPolicy(owner, request, operation);
        operation.Commit(candidate with { Levels = images.ToImmutable() });
    }

    /// <summary>Changes represented mip parameters, committing only after both commands complete.</summary>
    public void SetMipRange(GpuTexture owner, int baseLevel, int maxLevel)
    {
        RequireContext();
        owner.RequireOriginalContext();
        var previous = RHIResourceManager.AcquireTextureCommandInput(owner);
        if (baseLevel < 0 || maxLevel < 0 || previous.Target == TextureDimension.Rectangle && baseLevel != 0
            || previous.Target is TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray or TextureDimension.Buffer)
            throw new ArgumentOutOfRangeException(nameof(baseLevel));
        // Available canonical values already represent this request. Preserve dependent image
        // revisions instead of publishing a fictitious change during repeated owner setup.
        if (previous.BaseLevel == baseLevel && previous.MaxLevel == maxLevel) return;
        using var operation = new RHIResourceManager.MetadataOperation<GpuTextureMetadata>(owner, GpuMetadataChange.Metadata);
        int id = owner.TextureId;
        OpenGLDriverCalls.TextureParameter(id, Native.TextureParameterName.TextureBaseLevel, baseLevel);
        CheckTextureStep(operation);
        OpenGLDriverCalls.TextureParameter(id, Native.TextureParameterName.TextureMaxLevel, maxLevel);
        CheckTextureStep(operation);
        operation.Commit(previous with { BaseLevel = baseLevel, MaxLevel = maxLevel });
    }
    #endregion

    #region Internal API
    /// <summary>Initializes an unpublished specialized owner without exposing recipients in public factories.</summary>
    public void InitializeTextureOwner(GpuTexture owner, in RHITextureDescription description)
    {
        var request = description;
        RequireContext();
        var initialization = LowerTexture(request).Resolve();
        try
        {
            if (!RHIResourceManager.CreateTexture(initialization, owner, out _).HasValue)
                throw new InvalidOperationException("Texture initialization did not complete.");
            ApplyTextureFilterPolicy(owner, request);
            owner.SetDebugName(request.Label);
        }
        catch
        {
            // Keep the existing wrapper reusable, but never leave a failed candidate ID
            // installed. Cleanup belongs to execution and preserves the initialization error.
            int id = owner.TextureId;
            if (id != 0)
            {
                try
                {
                    owner.ResourceManager?.ReplaceWrapperResource(owner);
                    owner.SetCreationIdentity(0);
                    RetireResource(new(GpuResourceKind.Texture, id));
                }
                catch { owner.SetCreationIdentity(0); }
            }
            RHIResourceManager.PublishOwnerImages(owner, false);
            throw;
        }
    }
    #endregion

    #region Private
    /// <summary>Applies sampling fields as a standalone operation or an explicit step of storage replacement.</summary>
    private void ApplyTextureSampling(GpuTexture owner, in RHISamplerDescription description,
        RHIResourceManager.MetadataOperation<GpuTextureMetadata>? operation = null)
    {
        var request = description;
        RequireContext();
        owner.RequireOriginalContext();
        if (operation is null) _ = RHIResourceManager.RequireTextureReferenceMetadata(owner);
        else operation.RequireParticipation(owner, GpuMetadataChange.Metadata);
        ValidateSampler(request);
        if (request.Has(5) || request.Has(6) || request.Has(7) || request.Has(8) || request.Has(9) || request.Has(10) || request.Has(11))
            throw new ArgumentException("This owner operation accepts filtering and address modes.");
        int id = owner.TextureId;
        var guard = operation is null ? cache.EnterResourceOperation(new(GpuResourceKind.Texture, id)) : null;
        try
        {
            if (request.Has(0)) { OpenGLDriverCalls.TextureParameter(id, Native.TextureParameterName.TextureMinFilter, MinFilter(request.MinFilter)); if (operation is null) CheckTextureOwner(owner, id, guard!); else CheckTextureStep(operation); }
            if (request.Has(1)) { OpenGLDriverCalls.TextureParameter(id, Native.TextureParameterName.TextureMagFilter, MinFilter(request.MagFilter)); if (operation is null) CheckTextureOwner(owner, id, guard!); else CheckTextureStep(operation); }
            if (request.Has(2)) { OpenGLDriverCalls.TextureParameter(id, Native.TextureParameterName.TextureWrapS, Address(request.AddressS)); if (operation is null) CheckTextureOwner(owner, id, guard!); else CheckTextureStep(operation); }
            if (request.Has(3)) { OpenGLDriverCalls.TextureParameter(id, Native.TextureParameterName.TextureWrapT, Address(request.AddressT)); if (operation is null) CheckTextureOwner(owner, id, guard!); else CheckTextureStep(operation); }
            if (request.Has(4)) { OpenGLDriverCalls.TextureParameter(id, Native.TextureParameterName.TextureWrapR, Address(request.AddressR)); if (operation is null) CheckTextureOwner(owner, id, guard!); else CheckTextureStep(operation); }
        }
        finally { if (guard is not null) cache.ExitResourceOperation(guard); }
    }

    /// <summary>Preserves wrapper filtering policy while keeping parameter execution inside the backend.</summary>
    private void ApplyTextureFilterPolicy(GpuTexture owner, in RHITextureDescription request, RHIResourceManager.MetadataOperation<GpuTextureMetadata>? operation = null)
    {
        if (request.Dimension is TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray) return;
        bool linear = request.Filter == TextureFilterMode.Linear;
        ApplyTextureSampling(owner, new RHISamplerDescription { MinFilter = request.MipLevels > 1
            ? linear ? SamplerFilter.LinearMipLinear : SamplerFilter.NearestMipNearest
            : linear ? SamplerFilter.Linear : SamplerFilter.Nearest,
            MagFilter = linear ? SamplerFilter.Linear : SamplerFilter.Nearest,
            AddressS = SamplerAddressMode.ClampToEdge, AddressT = SamplerAddressMode.ClampToEdge, AddressR = SamplerAddressMode.ClampToEdge }, operation);
    }

    /// <summary>Stops compound setters after original identity or context changes.</summary>
    private void CheckTextureOwner(GpuTexture owner, int id, OpenGlStateCache.ResourceOperationGuard guard)
    {
        RequireContext();
        if (guard.Cancelled || !owner.IsValid || owner.TextureId != id) throw new InvalidOperationException("The original texture owner has retired or changed identity.");
    }
    #endregion
}
