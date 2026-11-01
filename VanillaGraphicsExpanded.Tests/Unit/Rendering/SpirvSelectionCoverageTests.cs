using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Guards packaged default-stage coverage supplied by the complete-program integration matrices.</summary>
public sealed class SpirvSelectionCoverageTests
{
    #region Public API
    /// <summary>Rejects catalog edits that leave a default specialization outside every complete-program selection.</summary>
    [Fact]
    public void CompleteProgramsCoverEveryPackagedDefaultSpecialization()
    {
        var registry = ShaderBuildTool.Spirv.TestShaderPrograms.Create();
        // Binary enumeration uses declaration defaults for numeric options, whereas complete
        // program assignments can select different values. Compare their effective inputs exactly.
        var selected = registry.Programs.Values.SelectMany(program => program.Assignments.SelectMany(assignment =>
            new ShaderLoadPlan(new ShaderSettings(program, assignment.ToDictionary(pair => pair.Key,
                pair => (string?)pair.Value.Canonical, StringComparer.Ordinal))).Stages))
            .GroupBy(stage => stage.Stage.Identity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var binary in registry.Binaries)
        {
            Assert.True(selected.TryGetValue(binary.Stage.Identity, out var candidates) &&
                candidates.Any(stage => stage.BinaryPath == binary.BinaryPath && stage.SameInputs(binary)),
                $"Packaged default {binary.Stage.Identity} [{binary.Key}] needs an explicit complete-program or native stage test.");
        }
    }
    #endregion
}
