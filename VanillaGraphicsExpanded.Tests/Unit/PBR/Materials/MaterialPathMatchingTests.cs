using System.Text.RegularExpressions;
using VanillaGraphicsExpanded.PBR.Materials;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Verifies material path wildcard matching within and across path segments.</summary>
public sealed class MaterialPathMatchingTests
{
    #region Public API
    /// <summary>Double-star wildcards match nested texture directories but preserve the required path shape.</summary>
    [Fact]
    public void Globstar_MatchesAcrossSegments_WithDoubleStar()
    {
        // Arrange: compile a pattern with repeated nested-directory wildcards.
        Regex regex = PbrGlobstar.CompileRegex("assets/**/textures/block/**/metal/**/*.png");

        // Act and assert: representative nested assets match while an unrelated texture path does not.
        Assert.Matches(regex, "assets/game/textures/block/metal/iron.png");
        Assert.Matches(regex, "assets/survival/textures/block/ore/metal/copper/ingot.png");
        Assert.DoesNotMatch(regex, "assets/game/textures/item/metal/iron.png");
    }

    /// <summary>Single-star wildcards stay within one path segment.</summary>
    [Fact]
    public void Star_DoesNotCrossSegments()
    {
        // Arrange: compile a pattern whose stars each represent one segment.
        Regex regex = PbrGlobstar.CompileRegex("assets/*/textures/*.png");

        // Act and assert: a direct file matches, but an extra directory does not.
        Assert.Matches(regex, "assets/game/textures/foo.png");
        Assert.DoesNotMatch(regex, "assets/game/textures/block/foo.png");
    }

    /// <summary>Question-mark wildcards match one character inside a path segment.</summary>
    [Fact]
    public void Question_MatchesSingleCharWithinSegment()
    {
        // Arrange: compile a pattern with one character wildcard in the filename.
        Regex regex = PbrGlobstar.CompileRegex("assets/game/textures/block/liquid/water?.png");

        // Act and assert: one suffix character matches while two do not.
        Assert.Matches(regex, "assets/game/textures/block/liquid/water1.png");
        Assert.DoesNotMatch(regex, "assets/game/textures/block/liquid/water12.png");
    }
    #endregion
}
