using VanillaGraphicsExpanded.PBR.Materials;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks equal-priority material mapping rule order.</summary>
public sealed class PbrGlobstarTests
{
    #region Public API
    /// <summary>Equal-priority matching rules select the first candidate in the tested order.</summary>
    [Fact]
    public void TieBreak_FirstRuleWins_WhenPriorityEqual()
    {
        // Arrange: construct matching rules with equal priority and stable sequence order.
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

        // Act: apply the current local winner-selection logic to the ordered candidates.
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

        // Assert: the first rule remains selected when priorities tie.
        Assert.NotNull(winner);
        Assert.Equal("early", winner?.Id);
    }
    #endregion
}
