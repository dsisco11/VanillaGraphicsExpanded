using System.Numerics;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Moq;
using System.Runtime.CompilerServices;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shadows;

/// <summary>Protects stable owner lifetimes, shared point influence and depth-inclusive receiving coverage.</summary>
public sealed class ShadowSourceAndCoverageTests
{
    #region Public API
    /// <summary>Movement preserves an owner token while replacement and world/dimension changes produce distinct keys.</summary>
    [Fact]
    public void SourceKeysFollowOwnersRatherThanPositions()
    {
        // Arrange: one retained native-owner token and a second owner at the same coordinates.
        var lifetimes = new ShadowSourceLifetimes();
        var owner = new object(); var replacement = new object();
        long lifetime = lifetimes.Get(owner);
        var first = new ShadowSourceKey(7, 0, ShadowSourceKind.Provider, lifetime, lifetime, 0);
        var moved = first with { };

        // Act: advance owner state without changing ownership, then explicitly replace it.
        long afterMovement = lifetimes.Get(owner);
        long replacementLifetime = lifetimes.Get(replacement);
        var otherWorld = first with { World = 8 };
        var otherDimension = first with { Dimension = 1 };

        // Assert: position is absent from provider identity and neither world nor dimension can alias.
        Assert.Equal(first, moved);
        Assert.Equal(first.Lifetime, afterMovement);
        Assert.NotEqual(first.Lifetime, replacementLifetime);
        Assert.NotEqual(first, otherWorld);
        Assert.NotEqual(first, otherDimension);
        Assert.True(first.IsKnown);
        Assert.False(default(ShadowSourceKey).IsKnown);
    }

    /// <summary>Player hands are separate slots and replacing the held stack changes its emission lifetime.</summary>
    [Fact]
    public void HandSlotsAndItemReplacementHaveIndependentIdentity()
    {
        // Arrange: the player and two successive stack owners remain at the same world position.
        var owners = new ShadowSourceLifetimes();
        var player = new object(); var rightStack = new object(); var replacement = new object();
        long playerLifetime = owners.Get(player);
        var right = new ShadowSourceKey(1, 0, ShadowSourceKind.Hand, playerLifetime, owners.Get(rightStack), 1);
        var left = right with { Slot = 2 };

        // Act: replace only the right-hand item while retaining the player.
        var replacedRight = right with { Lifetime = owners.Get(replacement) };

        // Assert: hand identity does not collapse to the player or attachment position.
        Assert.NotEqual(right, left);
        Assert.Equal(right.Owner, replacedRight.Owner);
        Assert.NotEqual(right.Lifetime, replacedRight.Lifetime);
    }

    /// <summary>Registered engine providers retain their object lifetime when their position changes.</summary>
    [Fact]
    public void NativeProviderIdentitySurvivesMovementAndSeparatesWorlds()
    {
        // Arrange: retain two actual engine world instances and two registered IPointLight owners.
        var firstGame = (ClientMain)RuntimeHelpers.GetUninitializedObject(typeof(ClientMain));
        var secondGame = (ClientMain)RuntimeHelpers.GetUninitializedObject(typeof(ClientMain));
        var position = new Vec3d(1, 2, 3);
        var providerMock = new Mock<IPointLight>();
        providerMock.SetupGet(source => source.Pos).Returns(position);
        var provider = providerMock.Object;
        var replacement = Mock.Of<IPointLight>();
        var firstWorld = NativeShadowLightSources.For(firstGame);

        // Act: move the actual native provider payload while preserving its registration owner.
        var firstPosition = (provider.Pos.X, provider.Pos.Y, provider.Pos.Z);
        var first = firstWorld.Provider(provider, 0);
        position.X = 9; position.Y = 8; position.Z = 7;
        var moved = firstWorld.Provider(provider, 0);
        var replaced = firstWorld.Provider(replacement, 0);
        var otherWorld = NativeShadowLightSources.For(secondGame).Provider(provider, 0);

        // Assert: stable provider identity follows registration ownership, scoped to a world and dimension.
        Assert.NotEqual(firstPosition, (provider.Pos.X, provider.Pos.Y, provider.Pos.Z));
        Assert.Equal(first, moved);
        Assert.NotEqual(first, replaced);
        Assert.NotEqual(first, otherWorld);
    }

    /// <summary>Entity lifetime and each held emission follow their native owners, not a shared player position.</summary>
    [Fact]
    public void NativeEntityAndHandKeysTrackReplacementOwners()
    {
        // Arrange: use native player, entity-position and item-stack instances without starting a game world.
        var game = (ClientMain)RuntimeHelpers.GetUninitializedObject(typeof(ClientMain));
        var sources = NativeShadowLightSources.For(game);
        var player = CreatePlayer(2);
        var replacementPlayer = CreatePlayer(2);
        var rightStack = CreateItemStack();
        var leftStack = CreateItemStack();

        // Act: recapture one owner, then replace the collectible inside the same held stack.
        var entityPosition = player.Pos;
        var initialEntityPosition = (entityPosition.X, entityPosition.Y, entityPosition.Z);
        var entity = sources.Entity(player);
        entityPosition.X += 12; entityPosition.Y += 3; entityPosition.Z -= 5;
        var movedEntity = sources.Entity(player);
        var replacementEntity = sources.Entity(replacementPlayer);
        var right = sources.Entity(player, 1, rightStack);
        var left = sources.Entity(player, 2, leftStack);
        SetItem(rightStack, (Item)RuntimeHelpers.GetUninitializedObject(typeof(Item)));
        var replacedRight = sources.Entity(player, 1, rightStack);

        // Assert: entity, hand slot, stack lifetime and world dimension all remain part of the key.
        Assert.NotEqual(initialEntityPosition, (player.Pos.X, player.Pos.Y, player.Pos.Z));
        Assert.Equal(2, entity.Dimension);
        Assert.Equal(entity, movedEntity);
        Assert.NotEqual(entity, replacementEntity);
        Assert.NotEqual(right, left);
        Assert.Equal(right.Owner, replacedRight.Owner);
        Assert.NotEqual(right.Lifetime, replacedRight.Lifetime);
        Assert.Equal(2, right.Dimension);
    }

    /// <summary>Point influence matches the native capped radius and authored positive-radius conventions.</summary>
    [Theory]
    [InlineData(3, 4, 0, 0, 5)]
    [InlineData(-3, 4, 0, 0, 4)]
    [InlineData(30, 40, 0, 0, 25)]
    [InlineData(0, 0, 0, 12, 12)]
    [InlineData(3, 4, 0, 9, 9)]
    public void InfluenceUsesSharedNativeOrAuthoredFormula(float x, float y, float z, float authoredRadius, float expected)
    {
        // Arrange: construct payloads at the engine and authored radius boundary.
        var light = new ShadowPointLight(default, Vector3.Zero, new(x, y, z, authoredRadius), WorldPosition: null);

        // Act: evaluate the contract used for both demand and attenuation support.
        float radius = light.Radius;

        // Assert: no independent shadow-only distance threshold is applied.
        Assert.Equal(expected, radius);
    }

    /// <summary>A newly requested receiver at another depth is outside the same page's certificate.</summary>
    [Fact]
    public void ReceiverCoverageIncludesDepthAndAppliedChangeHistory()
    {
        // Arrange: retain a stable page certificate while one known source moves within its coverage.
        var changes = new ShadowSceneChanges(4);
        var initial = changes.Capture(0, 1);
        var source = new ShadowSourceKey(7, 0, ShadowSourceKind.Entity, 2, 3, 0);
        var priorBounds = new ShadowBounds(1, 0, 1, 2, 1, 2);
        var currentBounds = new ShadowBounds(2, 0, 1, 3, 1, 2);
        var certificate = new ShadowReceivingCoverage(new(0, 0, 0, 4, 2, 4), source.Lifetime, 5, 7,
            initial.Sequence, initial.Epoch, 9);
        changes.BeginChange();
        var pending = changes.Capture(initial.Sequence, initial.Epoch);
        changes.CompleteChange(source, priorBounds, currentBounds, 11, 13, 17);
        var moved = changes.Capture(initial.Sequence, initial.Epoch);
        var samePageDifferentDepth = new ShadowBounds(1, 8, 1, 2, 9, 2);

        // Act: query cache validity before application, after application, and with the pending batch.
        var recorded = Assert.Single(moved.Changes);
        bool pendingCovered = certificate.Covers(new(1, 1, 1, 2, 2, 2), source.Lifetime, 5, 7, pending, 0);
        bool unappliedCovered = certificate.Covers(new(1, 1, 1, 2, 2, 2), source.Lifetime, 5, 7, moved, initial.Sequence);
        bool appliedCovered = certificate.Covers(new(1, 1, 1, 2, 2, 2), source.Lifetime, 5, 7, moved, moved.Sequence);
        bool newDepthCovered = certificate.Covers(samePageDifferentDepth, source.Lifetime, 5, 7, moved, moved.Sequence);

        // Assert: exact motion bounds/revisions survive publication; pending or unapplied work cannot certify reuse.
        Assert.Equal(priorBounds, recorded.Prior);
        Assert.Equal(currentBounds, recorded.Current);
        Assert.Equal(11, recorded.GeometryRevision);
        Assert.Equal(13, recorded.MaterialRevision);
        Assert.Equal(17, recorded.DeformationRevision);
        Assert.False(pending.Complete);
        Assert.True(moved.Complete);
        Assert.False(pendingCovered);
        Assert.False(unappliedCovered);
        Assert.True(appliedCovered);
        Assert.False(newDepthCovered);
    }

    /// <summary>Unfinished or lost scene history cannot certify reuse of old page depths.</summary>
    [Fact]
    public void ChangeOverflowAndPendingPublicationRequireBroadInvalidation()
    {
        // Arrange: retain one complete creation change, then overflow bounded history with another owner.
        var changes = new ShadowSceneChanges(1);
        var source = new ShadowSourceKey(1, 0, ShadowSourceKind.Entity, 2, 3, 0);
        changes.BeginChange();
        changes.CompleteChange(source, null, new(0, 0, 0, 1, 1, 1), 1, 1, 1);
        var first = changes.Capture(0, 1);
        changes.BeginChange();
        var pending = changes.Capture(first.Sequence, first.Epoch);
        changes.CompleteChange(source with { Owner = 4 }, null, new(2, 0, 0, 3, 1, 1), 1, 1, 1);

        // Act: query the overflowed retained generation from the prior page watermark.
        var overflow = changes.Capture(first.Sequence, first.Epoch);

        var certificate = new ShadowReceivingCoverage(new(0, 0, 0, 8, 2, 2), source.Lifetime, 5, 7,
            first.Sequence, first.Epoch, 9);
        bool lostHistoryCovered = certificate.Covers(new(0, 0, 0, 1, 1, 1), source.Lifetime,
            5, 7, overflow, overflow.Sequence);

        // Assert: neither partial work nor lost bounds are treated as no change by the page certificate.
        Assert.False(pending.Complete);
        Assert.False(first.RequiresBroadInvalidation);
        Assert.True(overflow.RequiresBroadInvalidation);
        Assert.False(lostHistoryCovered);
    }

    /// <summary>Input capacities remain user-configurable while malformed values return to bounded defaults.</summary>
    [Fact]
    public void InputSettingsRetainCustomCapsAndSanitizeInvalidValues()
    {
        // Arrange: retain valid user caps beside negative and above-maximum malformed values.
        var settings = new ShadowInputSettings
        {
            ChunkCapacity = 90000, RangeCapacity = -1, ChangeCapacity = 20000, ProbeLimit = 5000
        };

        // Act: apply the same field-level sanitizer used by the owning configuration object.
        settings.Sanitize();

        // Assert: valid controls remain configurable and invalid fields recover independently.
        Assert.Equal(90000, settings.ChunkCapacity);
        Assert.Equal(524288, settings.RangeCapacity);
        Assert.Equal(20000, settings.ChangeCapacity);
        Assert.Equal(128, settings.ProbeLimit);
    }
    #endregion

    #region Private
    /// <summary>Creates a native player whose actual EntityPos supplies a selected engine dimension.</summary>
    private static EntityPlayer CreatePlayer(int dimension)
    {
        var player = (EntityPlayer)RuntimeHelpers.GetUninitializedObject(typeof(EntityPlayer));
        var position = (EntityPos)RuntimeHelpers.GetUninitializedObject(typeof(EntityPos));
        typeof(EntityPos).GetField("Dimension", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(position, dimension);
        typeof(Entity).GetField("<Pos>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(player, position);
        return player;
    }

    /// <summary>Creates one ItemStack whose collectible can change independently of the stack object's lifetime.</summary>
    private static ItemStack CreateItemStack()
    {
        var stack = (ItemStack)RuntimeHelpers.GetUninitializedObject(typeof(ItemStack));
        SetItem(stack, (Item)RuntimeHelpers.GetUninitializedObject(typeof(Item)));
        return stack;
    }

    /// <summary>Changes only the engine stack's active collectible, preserving the ItemStack reference.</summary>
    private static void SetItem(ItemStack stack, Item item)
    {
        typeof(ItemStack).GetField("Class", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(stack, EnumItemClass.Item);
        typeof(ItemStack).GetField("item", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(stack, item);
    }
    #endregion
}
