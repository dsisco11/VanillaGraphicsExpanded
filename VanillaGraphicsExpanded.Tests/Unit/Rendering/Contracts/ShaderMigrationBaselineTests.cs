using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Compares generated declarations against the independently captured pre-migration compiled registry.</summary>
public sealed class ShaderMigrationBaselineTests
{
    #region Migration evidence
    /// <summary>All program settings, finite availability branches and binary paths retain the compiled baseline.</summary>
    [Fact]
    public void GeneratedDeclarationsPreserveCompiledBaseline()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "VanillaGraphicsExpanded.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        string path = Path.Combine(directory.FullName, "VanillaGraphicsExpanded.Tests", "Unit", "Rendering", "Contracts", "Fixtures", "ShaderMigrationBaseline.txt");
        Assert.Equal(File.ReadAllLines(path).Order(StringComparer.Ordinal), Snapshot(GpuShaderContracts.Registry).Order(StringComparer.Ordinal));
    }

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

