using System.Numerics;
using VanillaGraphicsExpanded.PBR.Clouds;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks that native source identity cannot outlive its exact publication or generation.</summary>
public sealed class CloudHierarchySourceTests
{
    #region Public API
    /// <summary>Every source field participates, even when a replacement reuses the same GL names.</summary>
    [Fact]
    public void ChangedDescriptorCannotReuseSummary()
    {
        var input = Input(new object());
        var source = new CloudHierarchySource(input);
        Assert.True(source.Matches(input));
        CloudMapInput[] changed =
        [
            input with { Owner = new object() }, input with { World = 2 },
            input with { Allocation = 2 }, input with { Upload = 2 }, input with { Shader = 2 },
            input with { Generation = 2 }, input with { Frame = 2 },
            input with { Storage = input.Storage with { Map = 10 } },
            input with { Storage = input.Storage with { Raw1 = 10 } },
            input with { Storage = input.Storage with { Raw2 = 10 } },
            input with { Storage = input.Storage with { Framebuffer = 10 } },
            input with { Storage = input.Storage with { Width = 81 } },
            input with { Storage = input.Storage with { TileSize = 51 } },
            input with { Storage = input.Storage with { Color = 10 } },
            input with { Placement = input.Placement with { CenterX = 3 } },
            input with { Placement = input.Placement with { CenterZ = 3 } },
            input with { Placement = input.Placement with { TileX = 3 } },
            input with { Placement = input.Placement with { TileZ = 3 } },
            input with { Placement = input.Placement with { WindTileX = 3 } },
            input with { Placement = input.Placement with { WindTileZ = 3 } },
            input with { Placement = input.Placement with { WindX = .25 } },
            input with { Placement = input.Placement with { WindZ = .25 } },
            input with { Placement = input.Placement with { Offset = Vector3.One } },
            input with { Placement = input.Placement with { Clock = 3 } }
        ];
        foreach (var current in changed) Assert.False(source.Matches(current));
    }

    /// <summary>Custom owner value equality does not join two native owner lifetimes.</summary>
    [Fact]
    public void OwnerUsesReferenceIdentity()
    {
        var input = Input("owner".ToCharArray());
        var first = Input(new EqualOwner());
        var second = first with { Owner = new EqualOwner() };
        Assert.Equal(first, second);
        Assert.False(new CloudHierarchySource(first).Matches(second));
        Assert.False(default(CloudHierarchySource).Matches(default));
        var unsupported = input with { Storage = input.Storage with { Width = 0 } };
        Assert.False(new CloudHierarchySource(unsupported).Matches(unsupported));
        var nonfinite = input with { Placement = input.Placement with { Clock = float.NaN } };
        Assert.False(new CloudHierarchySource(nonfinite).Matches(nonfinite));
    }

    /// <summary>GPU generations change identity without CPU uploads, and withdrawn publication never grants a borrow.</summary>
    [Fact]
    public void PublicationMustBeRevalidatedEvenWithoutWeatherUpload()
    {
        object owner = new();
        var input = Input(owner);
        var publication = new CloudMapPublication(owner, input.World);
        publication.BeginAllocation(); publication.CompleteAllocation(input.Storage, true);
        publication.BeginShader(); publication.CompleteShader(true);
        publication.BeginUpload(); publication.CompleteUpload(true);
        publication.BeginGeneration();
        publication.CompleteGeneration(7, input.Storage, input.Placement, true);
        Assert.True(publication.TryGet(owner, input.World, 7, out var first));
        var source = new CloudHierarchySource(first);
        Assert.True(source.Matches(first));
        publication.BeginGeneration();
        Assert.False(publication.IsCurrent(first, 7));
        Assert.False(publication.TryGet(owner, input.World, 7, out _));
        publication.CompleteGeneration(7, input.Storage, input.Placement, true);
        Assert.True(publication.TryGet(owner, input.World, 7, out var second));
        Assert.Equal(first.Upload, second.Upload);
        Assert.NotEqual(first.Generation, second.Generation);
        Assert.False(source.Matches(second));
        publication.Retire();
        Assert.False(publication.IsCurrent(second, 7));
    }
    #endregion

    #region Private
    /// <summary>Provides a coherent immutable native descriptor without executing native rendering.</summary>
    private static CloudMapInput Input(object owner) => new(owner, 1, 1, 1, 1, 1, 1,
        new(1, 2, 3, 4, 21, 50, 5), new(0, 0, 0, 0, 0, 0, 0, 0, Vector3.Zero, 0));

    /// <summary>Models a native owner whose value equality must not substitute for lifetime identity.</summary>
    private sealed class EqualOwner
    {
        #region Public API
        /// <summary>Deliberately considers distinct instances equivalent by value.</summary>
        public override bool Equals(object? obj) => obj is EqualOwner;
        /// <summary>Provides the hash required by the deliberately broad value equality.</summary>
        public override int GetHashCode() => 1;
        #endregion
    }
    #endregion
}
