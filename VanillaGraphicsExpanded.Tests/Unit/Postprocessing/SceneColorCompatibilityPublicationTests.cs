using VanillaGraphicsExpanded.PBR.SceneColor;

namespace VanillaGraphicsExpanded.Tests.Unit.Postprocessing;

/// <summary>Checks the connected native source publication and its late-draw admission contract.</summary>
public sealed class SceneColorCompatibilityPublicationTests
{
    #region Public API
    /// <summary>A completely dark ambient publication still admits the current native scene frame.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1e-30f)]
    public void DarkAmbientPublishesCurrentFiniteScene(float ambient)
    {
        // Arrange and act: create a matching native frame with the tested dark ambient input.
        var fog = SceneColorFogEnvelope.FromInputs(42, new(0), new(0), new(0), 0, []);
        var actual = SceneColorCompatibilityPublication.FromInputs(42, fog, 0, false, 0,
            new(ambient), 0, 0, 0, 0, 1, SceneColorPointLightBounds.FromEngine(0, []));
        // Assert: current finite scene data remains admissible without an artificial floor.
        Assert.Equal(42u, actual.FrameIndex);
        Assert.True(float.IsFinite(actual.MaximumNits) && actual.MaximumNits >= 0);
        actual.RequireWithin(actual);
    }

    /// <summary>Concrete fog, cloud geometry, native point and normalized asset endpoints produce one finite physical budget.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CombinedNativeSourcesPublishFiniteMatchingBudget(bool mapEnabled)
    {
        // Arrange: combine concrete fog, cloud, ambient, map, and point-light endpoints.
        var points = SceneColorPointLightBounds.FromEngine(2, [2, 9, 3, 200, 400, 100]);
        var fog = SceneColorFogEnvelope.FromInputs(42, new(1, 2, 3), new(2, 4, 1), new(3, 1, 5), 0, []);
        var cloud = SceneColorCloudInputs.FromInputs(42, 64, 20, 1, 0, 2, 3, 0, 0);
        // Act: compose all authored native sources into the frame publication.
        var actual = SceneColorCompatibilityPublication.FromInputs(42, fog, cloud.VertexMaximum, mapEnabled, 3,
            new(1, 3, 2), 7, .4f, .5f, 2, 3, points);
        // Assert: the publication retains frame identity, finite component bounds, and their total budget.
        Assert.Equal(42u, actual.FrameIndex); Assert.Equal(fog.MaximumNits, actual.Fog);
        Assert.True(actual.Sky > 0 && actual.Stars > 0 && actual.Moon > 0 && actual.Particles > 0 && actual.Clouds > 0);
        Assert.Equal(mapEnabled, actual.CloudMap > 0);
        double sum = (double)actual.Fog + actual.Sky + actual.Stars + actual.Moon + actual.Particles + actual.Clouds + actual.CloudMap + 32;
        Assert.InRange(actual.MaximumNits, sum, sum * 1.000001);
        actual.RequireWithin(actual);
    }

    /// <summary>New native point lights cannot be bound against the earlier weaker immutable frame envelope.</summary>
    [Fact]
    public void LateIncreasedNativePointEnvelopeIsRejected()
    {
        // Arrange: provide one fog frame and distinct weak/strong native point-light inputs.
        var fog = SceneColorFogEnvelope.FromInputs(42, new(1), new(0), new(0), 0, []);
        // Act: build two immutable publications from the same frame with different source envelopes.
        var earlier = SceneColorCompatibilityPublication.FromInputs(42, fog, 3, true, 2, new(1, 3, 2), 1, 1, 0, 0, 1,
            SceneColorPointLightBounds.FromEngine(0, []));
        var later = SceneColorCompatibilityPublication.FromInputs(42, fog, 3, true, 2, new(1, 3, 2), 1, 1, 0, 0, 1,
            SceneColorPointLightBounds.FromEngine(1, [200, 400, 100]));
        // Assert: later growth is rejected against the earlier immutable envelope.
        Assert.True(later.Particles > earlier.Particles || later.Clouds > earlier.Clouds || later.CloudMap > earlier.CloudMap);
        Assert.Throws<InvalidOperationException>(() => later.RequireWithin(earlier));
        earlier.RequireWithin(later);
    }

    /// <summary>Matching finite values cannot cross a frame identity or use mismatched source metadata.</summary>
    [Fact]
    public void FrameIdentityAndSourceAdmissionAreRequired()
    {
        // Arrange: provide empty native point sources and matching/mismatched fog frame identities.
        var fog = SceneColorFogEnvelope.FromInputs(42, new(1), new(0), new(0), 0, []);
        var points = SceneColorPointLightBounds.FromEngine(0, []);
        // Act: construct the admitted frame and a later frame carrying mismatched source metadata.
        var admitted = SceneColorCompatibilityPublication.FromInputs(42, fog, 3, false, 0, new(1, 3, 2), 1, 1, 0, 0, 1, points);
        var next = SceneColorCompatibilityPublication.FromInputs(43, fog with { FrameIndex = 43 }, 3, false, 0, new(1, 3, 2), 1, 1, 0, 0, 1, points);
        // Assert: frame mismatch and unsupported source metadata cannot pass admission.
        Assert.Throws<InvalidOperationException>(() => next.RequireWithin(admitted));
        Assert.Throws<ArgumentException>(() => SceneColorCompatibilityPublication.FromInputs(43, fog, 3, false, 0, new(1, 3, 2), 1, 1, 0, 0, 1, points));
        Assert.Throws<ArgumentException>(() => SceneColorCompatibilityPublication.FromInputs(42, fog, 3, false, 0, new(1, 3, 2), 1, 1, 0, 0, 1, default));
    }
    #endregion
}

