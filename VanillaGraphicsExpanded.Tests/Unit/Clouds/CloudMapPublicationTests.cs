using System.Numerics;
using VanillaGraphicsExpanded.PBR.Clouds;

namespace VanillaGraphicsExpanded.Tests.Unit.Clouds;

/// <summary>Exercises content certificates with reusable native names and deterministic producer events.</summary>
public sealed class CloudMapPublicationTests
{
    private static readonly CloudMapStorage Storage = new(1, 2, 3, 4, 20, 50, 5);
    private static readonly CloudPlacement Placement = new(3, 4, 1, 2, 8, 9, .25, .5, new Vector3(1, 20, 2), 40);

    #region Public API
    #region Content readiness
    /// <summary>Allocated/linked resources are insufficient, even after a successful generation.</summary>
    [Fact]
    public void LateAcquisitionWaitsForObservedUpload()
    {
        // Arrange: prepare allocation and shader readiness without upload evidence.
        object owner = new(); var publication = Prepared(owner);
        // Act: complete generation first, then attempt to borrow the map.
        Generate(publication);
        // Assert: the descriptor stays unavailable until upload and generation both complete.
        Assert.False(publication.TryGet(owner, 10, 7, out _));
        // Act: publish the map only after upload evidence arrives.
        publication.BeginUpload(); publication.CompleteUpload(true); Generate(publication);
        // Assert: the successful upload makes the completed generation borrowable.
        Assert.True(publication.TryGet(owner, 10, 7, out var input));
        Assert.Equal(Placement, input.Placement);
        Assert.Equal(1, CloudMapInput.MetresPerWorldUnit);
    }

    /// <summary>Mode-gated, failed and next-frame callbacks cannot reuse the previous map.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryEntryWithdrawsPreviousPublication(bool finishFailure)
    {
        // Arrange: start with a published descriptor and choose whether the next generation fails.
        object owner = new(); var publication = Ready(owner);
        Assert.True(publication.TryGet(owner, 10, 7, out var old));
        Assert.False(publication.TryGet(owner, 10, 8, out _));
        // Act: beginning any new generation withdraws the previous descriptor.
        publication.BeginGeneration();
        if (finishFailure) publication.CompleteGeneration(7, Storage, Placement, false);
        // Assert: the stale descriptor and any failed generation remain unavailable.
        Assert.False(publication.IsCurrent(old, 7));
        Assert.False(publication.TryGet(owner, 10, 7, out _));
        // Act: a fresh generation may reuse valid controls, never the failed map contents.
        Generate(publication);
        // Assert: reacquisition uses a new generation identity.
        Assert.True(publication.TryGet(owner, 10, 7, out var fresh));
        Assert.NotEqual(old.Generation, fresh.Generation);
    }

    /// <summary>In-place upload failure destroys the old raw-data certificate until both uploads succeed again.</summary>
    [Fact]
    public void FailedUploadCannotInheritPreviousContent()
    {
        // Arrange: begin with a valid published upload.
        object owner = new(); var publication = Ready(owner);
        // Act: invalidate content with a failed in-place upload.
        publication.BeginUpload(); publication.CompleteUpload(false); Generate(publication);
        // Assert: old bytes are unavailable until a later successful upload.
        Assert.False(publication.TryGet(owner, 10, 7, out _));
        // Act: complete a later successful upload.
        publication.BeginUpload(); publication.CompleteUpload(true); Generate(publication);
        // Assert: only the newly uploaded content is available.
        Assert.True(publication.TryGet(owner, 10, 7, out _));
    }
    #endregion

    #region Replacement and paused generation
    /// <summary>Same-size recreation still requires an upload and produces a different allocation identity.</summary>
    [Fact]
    public void IdenticalGlNamesDoNotPreserveAllocationIdentity()
    {
        // Arrange: retain a descriptor for the current allocation.
        object owner = new(); var publication = Ready(owner);
        Assert.True(publication.TryGet(owner, 10, 7, out var old));
        // Act: recreate storage with identical dimensions and native names.
        publication.BeginAllocation(); publication.CompleteAllocation(Storage, true); Generate(publication);
        // Assert: the old generation is stale until a new upload completes.
        Assert.False(publication.IsCurrent(old, 7));
        // Act: complete the required upload for the recreated allocation.
        publication.BeginUpload(); publication.CompleteUpload(true); Generate(publication);
        // Assert: identical native names retain values but receive a new allocation identity.
        Assert.True(publication.TryGet(owner, 10, 7, out var current));
        Assert.Equal(old.Storage, current.Storage);
        Assert.NotEqual(old.Allocation, current.Allocation);
    }

    /// <summary>A changed handle or dimension observed without an allocation callback fails closed.</summary>
    [Theory]
    [InlineData(8, 20)]
    [InlineData(1, 40)]
    public void UnobservedReplacementRejectsBorrow(int map, int width)
    {
        // Arrange: change either the map handle or width without an allocation callback.
        object owner = new(); var publication = Ready(owner);
        // Act: publish the changed generation and request the prior allocation identity.
        publication.BeginGeneration(); publication.CompleteGeneration(7, Storage with { Map = map, Width = width }, Placement, true);
        // Assert: replacement without allocation evidence cannot be borrowed.
        Assert.False(publication.TryGet(owner, 10, 7, out _));
    }

    /// <summary>Reload invalidates map generation while retaining raw content in the unchanged allocation.</summary>
    [Fact]
    public void ReloadRequiresSuccessfulProgramAndNewGeneration()
    {
        // Arrange: retain the currently published descriptor.
        object owner = new(); var publication = Ready(owner);
        Assert.True(publication.TryGet(owner, 10, 7, out var old));
        // Act: fail a reload, then complete another shader reload without regenerating content.
        publication.BeginShader(); publication.CompleteShader(false); Generate(publication);
        // Assert: neither reload path exposes stale readiness.
        Assert.False(publication.TryGet(owner, 10, 7, out _));
        // Act: finish a successful shader reload without regenerating the map.
        publication.BeginShader(); publication.CompleteShader(true);
        // Assert: a new map generation is still required.
        Assert.False(publication.TryGet(owner, 10, 7, out _));
        // Act: publish the regenerated map under the successfully reloaded shader.
        Generate(publication);
        // Assert: raw upload identity remains while shader and map generation advance.
        Assert.True(publication.TryGet(owner, 10, 7, out var current));
        Assert.Equal(old.Upload, current.Upload);
        Assert.NotEqual(old.Shader, current.Shader);
    }

    /// <summary>Paused generation updates placement without requiring a new tile upload or clock advance.</summary>
    [Fact]
    public void PausedCameraMovementPublishesMatchingPlacement()
    {
        // Arrange: retain previous content and author a camera-relative placement update.
        object owner = new(); var publication = Ready(owner);
        Assert.True(publication.TryGet(owner, 10, 7, out var old));
        var paused = Placement with { Offset = new(30, 25, -5) };
        // Act: complete the placement update without changing storage or upload generation.
        publication.BeginGeneration(); publication.CompleteGeneration(8, Storage, paused, true);
        // Assert: the new descriptor carries placement while reusing uploaded content.
        Assert.True(publication.TryGet(owner, 10, 8, out var current));
        Assert.Equal(old.Upload, current.Upload); Assert.Equal(old.Placement.Clock, current.Placement.Clock);
        Assert.Equal(paused, current.Placement); Assert.False(publication.IsCurrent(old, 8));
    }
    #endregion

    #region Invalid identities and inputs
    /// <summary>World and owner identity are mandatory even if all native names are reused.</summary>
    [Fact]
    public void RetiredAndForeignOwnersNeverReacquireContent()
    {
        // Arrange: start with one owner holding a published descriptor.
        object owner = new(); var publication = Ready(owner);
        // Act/Assert: reject foreign identities before retirement.
        Assert.False(publication.TryGet(new object(), 10, 7, out _));
        Assert.False(publication.TryGet(owner, 11, 7, out _));
        // Act/Assert: retirement rejects all later callbacks and reacquisition.
        publication.Retire(); publication.BeginAllocation(); publication.CompleteAllocation(Storage, true);
        publication.BeginShader(); publication.CompleteShader(true);
        publication.BeginUpload(); publication.CompleteUpload(true); Generate(publication);
        Assert.False(publication.TryGet(owner, 10, 7, out _));
    }

    /// <summary>Invalid native storage and placement cannot become a consumable descriptor.</summary>
    [Theory]
    [InlineData(0, 20, 50, 1)]
    [InlineData(1, 0, 50, 1)]
    [InlineData(1, 201, 50, 1)]
    [InlineData(1, 20, 64, 1)]
    [InlineData(1, 20, 50, float.NaN)]
    public void InvalidInputsAreUnavailable(int map, int width, int tile, float offset)
    {
        // Arrange: substitute one invalid storage or placement field in an otherwise valid setup.
        object owner = new(); var publication = new CloudMapPublication(owner, 10);
        var storage = Storage with { Map = map, Width = width, TileSize = tile };
        publication.BeginAllocation(); publication.CompleteAllocation(storage, true);
        publication.CompleteShader(true); publication.CompleteUpload(true);
        // Act: feed the invalid descriptor through the complete callback sequence.
        publication.CompleteGeneration(7, storage, Placement with { Offset = new(offset, 0, 0) }, true);
        // Assert: invalid inputs never produce a borrowable descriptor.
        Assert.False(publication.TryGet(owner, 10, 7, out _));
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Prepares storage and program without claiming an upload.</summary>
    private static CloudMapPublication Prepared(object owner)
    {
        var publication = new CloudMapPublication(owner, 10);
        publication.BeginAllocation(); publication.CompleteAllocation(Storage, true);
        publication.BeginShader(); publication.CompleteShader(true); return publication;
    }
    /// <summary>Completes a native upload and generation for subsequent invalidation scenarios.</summary>
    private static CloudMapPublication Ready(object owner)
    {
        var publication = Prepared(owner); publication.BeginUpload(); publication.CompleteUpload(true);
        Generate(publication); return publication;
    }
    /// <summary>Models one complete selected native generation callback.</summary>
    private static void Generate(CloudMapPublication publication)
    {
        publication.BeginGeneration(); publication.CompleteGeneration(7, Storage, Placement, true);
    }
    #endregion
}
