using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Compares generated declarations against the checked-in canonical shader catalog snapshot.</summary>
public sealed class ShaderMigrationBaselineTests
{
    #region Public API
    #region Migration evidence
    /// <summary>Program settings, finite availability branches and binary paths match the current catalog contract.</summary>
    [Fact]
    public void GeneratedDeclarationsMatchCatalogSnapshot()
    {
        // Arrange: Load the frozen semantic rows from the build inputs.
        string[] expected = TestAssetSnapshot.Built.Lines("test-inputs/baselines/ShaderMigrationBaseline.txt");
        // Act: Project the generated registry into stable program, stage, assignment, and binary rows.
        string[] actual = Snapshot(GpuShaderContracts.Registry).Order(StringComparer.Ordinal).ToArray();
        // Assert: Generated declarations still match the checked-in catalog baseline.
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Produces stable semantic rows rather than depending on declaration order or generated C# formatting.</summary>
    private static IEnumerable<string> Snapshot(ShaderVariantResolver registry)
    {
        foreach (var program in registry.Programs.Values)
        {
            yield return $"P|{program.Identity}|{program.VariantBudget}";
            foreach (var group in program.Groups)
                yield return $"G|{program.Identity}|{group.Identity}|{string.Join(',', group.Options.Select(o => o.Name).Order(StringComparer.Ordinal))}";
            foreach (var option in program.Options)
                yield return $"O|{program.Identity}|{option.Name}|{option.ScalarType}|{option.Default.Canonical}|{string.Join(',', option.Aliases.Order(StringComparer.Ordinal))}|{string.Join(',', option.Domain?.Select(v => v.Canonical) ?? [])}|{option.Minimum?.Canonical}|{option.Maximum?.Canonical}";
            foreach (var stage in program.Stages)
            {
                yield return $"S|{program.Identity}|{stage.Identity}|{stage.Source}|{stage.Kind}|{stage.EntryPoint}|{stage.BinaryAsset}";
                foreach (var constant in stage.Specializations) yield return $"C|{program.Identity}|{stage.Identity}|{constant.Id}|{constant.Option.Name}";
                foreach (var assignment in program.Assignments)
                {
                    string key = string.Join(';', assignment.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value.Canonical}"));
                    string active = string.Join(',', stage.Specializations.Where(s => s.Condition?.Evaluate(assignment) ?? true).Select(s => s.Id));
                    yield return $"A|{program.Identity}|{stage.Identity}|{key}|{active}";
                }
            }
        }
        foreach (var binary in registry.Binaries) yield return $"B|{binary.Stage.Identity}|{binary.Key}|{binary.BinaryPath}";
    }
    #endregion
}

