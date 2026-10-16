using System.Numerics;
using VanillaGraphicsExpanded.PBR.Clouds;

namespace VanillaGraphicsExpanded.Tests;

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
        object owner = new(); var publication = Prepared(owner);
        Generate(publication);
        Assert.False(publication.TryGet(owner, 10, 7, out _));
        publication.BeginUpload(); publication.CompleteUpload(true); Generate(publication);
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
        object owner = new(); var publication = Ready(owner);
        Assert.True(publication.TryGet(owner, 10, 7, out var old));
        Assert.False(publication.TryGet(owner, 10, 8, out _));
        publication.BeginGeneration();
        if (finishFailure) publication.CompleteGeneration(7, Storage, Placement, false);
        Assert.False(publication.IsCurrent(old, 7));
        Assert.False(publication.TryGet(owner, 10, 7, out _));
        // A fresh generation may reuse valid raw controls, never the failed map contents.
        Generate(publication);
        Assert.True(publication.TryGet(owner, 10, 7, out var fresh));
        Assert.NotEqual(old.Generation, fresh.Generation);
    }

    /// <summary>In-place upload failure destroys the old raw-data certificate until both uploads succeed again.</summary>
    [Fact]
    public void FailedUploadCannotInheritPreviousContent()
    {
        object owner = new(); var publication = Ready(owner);
        publication.BeginUpload(); publication.CompleteUpload(false); Generate(publication);
        Assert.False(publication.TryGet(owner, 10, 7, out _));
        publication.BeginUpload(); publication.CompleteUpload(true); Generate(publication);
        Assert.True(publication.TryGet(owner, 10, 7, out _));
    }
    #endregion

    #region Replacement and paused generation
    /// <summary>Same-size recreation still requires an upload and produces a different allocation identity.</summary>
    [Fact]
    public void IdenticalGlNamesDoNotPreserveAllocationIdentity()
    {
        object owner = new(); var publication = Ready(owner);
        Assert.True(publication.TryGet(owner, 10, 7, out var old));
        publication.BeginAllocation(); publication.CompleteAllocation(Storage, true); Generate(publication);
        Assert.False(publication.IsCurrent(old, 7));
        publication.BeginUpload(); publication.CompleteUpload(true); Generate(publication);
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
        object owner = new(); var publication = Ready(owner);
        publication.BeginGeneration(); publication.CompleteGeneration(7, Storage with { Map = map, Width = width }, Placement, true);
        Assert.False(publication.TryGet(owner, 10, 7, out _));
    }

    /// <summary>Reload invalidates map generation while retaining raw content in the unchanged allocation.</summary>
    [Fact]
    public void ReloadRequiresSuccessfulProgramAndNewGeneration()
    {
        object owner = new(); var publication = Ready(owner);
        Assert.True(publication.TryGet(owner, 10, 7, out var old));
        publication.BeginShader(); publication.CompleteShader(false); Generate(publication);
        Assert.False(publication.TryGet(owner, 10, 7, out _));
        publication.BeginShader(); publication.CompleteShader(true);
        Assert.False(publication.TryGet(owner, 10, 7, out _));
        Generate(publication);
        Assert.True(publication.TryGet(owner, 10, 7, out var current));
        Assert.Equal(old.Upload, current.Upload);
        Assert.NotEqual(old.Shader, current.Shader);
    }

    /// <summary>Paused generation updates placement without requiring a new tile upload or clock advance.</summary>
    [Fact]
    public void PausedCameraMovementPublishesMatchingPlacement()
    {
        object owner = new(); var publication = Ready(owner);
        Assert.True(publication.TryGet(owner, 10, 7, out var old));
        var paused = Placement with { Offset = new(30, 25, -5) };
        publication.BeginGeneration(); publication.CompleteGeneration(8, Storage, paused, true);
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
        object owner = new(); var publication = Ready(owner);
        Assert.False(publication.TryGet(new object(), 10, 7, out _));
        Assert.False(publication.TryGet(owner, 11, 7, out _));
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
        object owner = new(); var publication = new CloudMapPublication(owner, 10);
        var storage = Storage with { Map = map, Width = width, TileSize = tile };
        publication.BeginAllocation(); publication.CompleteAllocation(storage, true);
        publication.CompleteShader(true); publication.CompleteUpload(true);
        publication.CompleteGeneration(7, storage, Placement with { Offset = new(offset, 0, 0) }, true);
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
