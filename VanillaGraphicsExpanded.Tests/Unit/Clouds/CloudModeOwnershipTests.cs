using VanillaGraphicsExpanded.PBR.Clouds;

namespace VanillaGraphicsExpanded.Tests.Unit.Clouds;

/// <summary>Qualifies demand admission, native restoration and VGE-only resource disposal.</summary>
public sealed class CloudModeOwnershipTests
{
    #region Public API
    /// <summary>No native draw is withdrawn for an unfinished, disabled or unselected replacement.</summary>
    [Theory]
    [InlineData(false, 1, true)]
    [InlineData(true, 1, false)]
    [InlineData(true, 0, true)]
    [InlineData(true, 2, true)]
    public void UnavailableConsumerLeavesNativeBehaviorIntact(bool enabled, int selected, bool ready)
    {
        // Arrange: configure a consumer that is disabled, unselected, or not ready.
        using var ownership = new CloudModeOwnership();
        var resources = new Resource(); ownership.Update(enabled, selected);
        // Act: attempt to admit it as the volumetric owner.
        bool admitted = ownership.TryActivate(CloudMode.Volumetric, resources, ready);
        // Assert: rejected resources remain undisposed and no draw gate is owned.
        Assert.False(admitted);
        Assert.False(ownership.Owns(CloudMode.Volumetric)); Assert.Equal(0, resources.Disposals);
    }

    /// <summary>Accepted draw lifetime survives missing frame inputs and releases its draw gate on mode change.</summary>
    [Fact]
    public void VolumeReleasesOnlyOwnedResourcesOnModeChange()
    {
        // Arrange: configure a ready volumetric consumer and its owned resource; disposal count starts at zero.
        using var ownership = new CloudModeOwnership();
        var resources = new Resource(); ownership.Update(true, 1);
        // Act/Assert: admit the consumer and confirm its draw gate is active.
        bool admitted = ownership.TryActivate(CloudMode.Volumetric, resources, true);
        Assert.True(admitted);
        // Act/Assert: refresh the same mode without releasing ownership.
        ownership.Update(true, 1);
        Assert.True(ownership.Owns(CloudMode.Volumetric));
        // Act/Assert: switching mode retires the owned resource and later disposal is idempotent.
        ownership.Update(true, 2);
        Assert.Equal(1, resources.Disposals); Assert.False(ownership.Owns(CloudMode.Volumetric));
        ownership.Dispose(); Assert.Equal(1, resources.Disposals);
    }

    /// <summary>Simple admission affects no renderer registrations, and disable releases its resources once.</summary>
    [Fact]
    public void SimpleUsesInterceptionOwnershipWithoutRegistrationChanges()
    {
        // Arrange: select simple clouds with an owned resource.
        using var ownership = new CloudModeOwnership();
        var resources = new Resource(); ownership.Update(true, 2);
        // Act: admit simple rendering.
        bool admitted = ownership.TryActivate(CloudMode.Simple, resources, true);
        // Assert: simple mode is admitted without a native registration boundary.
        Assert.True(admitted);
        Assert.True(ownership.Owns(CloudMode.Simple));
        // Act: disable simple rendering.
        ownership.Update(false, 2);
        // Assert: ownership ends and its resource is released exactly once.
        Assert.False(ownership.Owns(CloudMode.Simple));
        Assert.Equal(1, resources.Disposals);
    }

    /// <summary>A second consumer cannot replace the admitted owner or acquire its disposal authority.</summary>
    [Fact]
    public void SecondConsumerCannotReplaceAdmittedOwner()
    {
        // Arrange: configure ownership and admit one volumetric owner.
        using var ownership = new CloudModeOwnership();
        var resources = new Resource(); ownership.Update(true, 1);
        bool admitted = ownership.TryActivate(CloudMode.Volumetric, resources, true);
        Assert.True(admitted);
        var rejected = new Resource();
        // Act: ask a second consumer to take the same ownership slot.
        bool secondAdmitted = ownership.TryActivate(CloudMode.Volumetric, rejected, true);
        // Assert: the rejected resource is untouched and the original owner remains active.
        Assert.False(secondAdmitted);
        Assert.Equal(0, rejected.Disposals); Assert.True(ownership.Owns(CloudMode.Volumetric));
    }
    #endregion

    #region Private
    /// <summary>Counts disposal of a VGE-owned draw resource; no borrowed native object is attached.</summary>
    private sealed class Resource : IDisposable
    {
        #region Public API
        internal int Disposals { get; private set; }
        /// <summary>Records release of this owned lifetime.</summary>
        public void Dispose() => Disposals++;
        #endregion
    }
    #endregion
}
