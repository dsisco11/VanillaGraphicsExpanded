using VanillaGraphicsExpanded.PBR.Clouds;

namespace VanillaGraphicsExpanded.Tests;

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
        using var ownership = new CloudModeOwnership();
        var resources = new Resource(); ownership.Update(enabled, selected);
        Assert.False(ownership.TryActivate(CloudMode.Volumetric, resources, ready));
        Assert.False(ownership.Owns(CloudMode.Volumetric)); Assert.Equal(0, resources.Disposals);
    }

    /// <summary>Accepted draw lifetime survives missing frame inputs and releases its draw gate on mode change.</summary>
    [Fact]
    public void VolumeReleasesOnlyOwnedResourcesOnModeChange()
    {
        using var ownership = new CloudModeOwnership();
        var resources = new Resource(); ownership.Update(true, 1);
        Assert.True(ownership.TryActivate(CloudMode.Volumetric, resources, true));
        ownership.Update(true, 1); Assert.True(ownership.Owns(CloudMode.Volumetric));
        ownership.Update(true, 2); Assert.Equal(1, resources.Disposals); Assert.False(ownership.Owns(CloudMode.Volumetric));
        ownership.Dispose(); Assert.Equal(1, resources.Disposals);
    }

    /// <summary>Simple admission affects no renderer registrations, and disable releases its resources once.</summary>
    [Fact]
    public void SimpleUsesInterceptionOwnershipWithoutRegistrationChanges()
    {
        using var ownership = new CloudModeOwnership();
        var resources = new Resource(); ownership.Update(true, 2);
        Assert.True(ownership.TryActivate(CloudMode.Simple, resources, true));
        Assert.True(ownership.Owns(CloudMode.Simple));
        ownership.Update(false, 2); Assert.False(ownership.Owns(CloudMode.Simple));
        Assert.Equal(1, resources.Disposals);
    }

    /// <summary>A second consumer cannot replace the admitted owner or acquire its disposal authority.</summary>
    [Fact]
    public void SecondConsumerCannotReplaceAdmittedOwner()
    {
        using var ownership = new CloudModeOwnership();
        var resources = new Resource(); ownership.Update(true, 1);
        Assert.True(ownership.TryActivate(CloudMode.Volumetric, resources, true));
        var rejected = new Resource();
        Assert.False(ownership.TryActivate(CloudMode.Volumetric, rejected, true));
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
