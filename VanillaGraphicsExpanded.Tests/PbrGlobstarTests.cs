using System.Text.RegularExpressions;

using VanillaGraphicsExpanded.PBR.Materials;

namespace VanillaGraphicsExpanded.Tests;

public sealed class PbrGlobstarTests
{
    [Fact]
    public void Globstar_MatchesAcrossSegments_WithDoubleStar()
    {
        Regex regex = PbrGlobstar.CompileRegex("assets/**/textures/block/**/metal/**/*.png");

        Assert.Matches(regex, "assets/game/textures/block/metal/iron.png");
        Assert.Matches(regex, "assets/survival/textures/block/ore/metal/copper/ingot.png");
        Assert.DoesNotMatch(regex, "assets/game/textures/item/metal/iron.png");
    }

    [Fact]
    public void Star_DoesNotCrossSegments()
    {
        Regex regex = PbrGlobstar.CompileRegex("assets/*/textures/*.png");

        Assert.Matches(regex, "assets/game/textures/foo.png");
        Assert.DoesNotMatch(regex, "assets/game/textures/block/foo.png");
    }

    [Fact]
    public void Question_MatchesSingleCharWithinSegment()
    {
        Regex regex = PbrGlobstar.CompileRegex("assets/game/textures/block/liquid/water?.png");

        Assert.Matches(regex, "assets/game/textures/block/liquid/water1.png");
        Assert.DoesNotMatch(regex, "assets/game/textures/block/liquid/water12.png");
    }

    [Fact]
    public void TieBreak_FirstRuleWins_WhenPriorityEqual()
    {
        // First matching rule should win when priority ties.
        var early = new PbrMaterialMappingRule(
            OrderIndex: 1,
            Priority: 0,
            Source: new("game", "config/vge/material_definitions.json"),
            Id: "early",
            Glob: "assets/**/textures/block/liquid/water*.png",
            MatchRegex: PbrGlobstar.CompileRegex("assets/**/textures/block/liquid/water*.png"),
            MaterialId: "game:water",
            OverrideMaterialParams: null,
            OverrideNormalHeight: null,
            OverrideScale: PbrOverrideScale.Identity);

        var later = new PbrMaterialMappingRule(
            OrderIndex: 2,
            Priority: 0,
            Source: new("game", "config/vge/material_definitions.json"),
            Id: "later",
            Glob: "assets/**/textures/block/liquid/water*.png",
            MatchRegex: PbrGlobstar.CompileRegex("assets/**/textures/block/liquid/water*.png"),
            MaterialId: "game:lava",
            OverrideMaterialParams: null,
            OverrideNormalHeight: null,
            OverrideScale: PbrOverrideScale.Identity);

        const string key = "assets/game/textures/block/liquid/water.png";

        PbrMaterialMappingRule? winner = null;
        foreach (PbrMaterialMappingRule rule in new[] { early, later })
        {
            if (!rule.MatchRegex.IsMatch(key)) continue;

            if (winner == null)
            {
                winner = rule;
                continue;
            }

            if (rule.Priority > winner.Value.Priority)
            {
                winner = rule;
            }
        }

        Assert.NotNull(winner);
        Assert.Equal("early", winner?.Id);
    }
}
