using Microsoft.CodeAnalysis;

namespace ShaderContractGenerator.Tests;

/// <summary>Requires actionable compile-time diagnostics for invalid shader declarations.</summary>
public sealed class DiagnosticTests
{
    #region Invalid metadata
    /// <summary>Rejects invalid accessor shapes, scalar metadata and finite-domain declarations.</summary>
    [Theory]
    [InlineData("internal static partial ShaderOption<bool> Enabled { get; }", "internal static ShaderOption<bool> Enabled { get; }", "partial")]
    [InlineData("ShaderOption<bool>", "ShaderOption<double>", "type")]
    [InlineData("false, Aliases", "1, Aliases", "type")]
    [InlineData("\"ENABLED\"", "\"NOT-VALID\"", "identifier")]
    [InlineData("\"OLD_ENABLED\"", "\"ENABLED\"", "aliases")]
    [InlineData("\"example\", 2", "\"example\", 1", "budget")]
    [InlineData("ShaderOption<bool>", "ShaderOption<int>", "type")]
    public void InvalidOptionsFailAtOwner(string before, string after, string expected)
    {
        AssertDiagnostic(DeclarationTests.Basic.Replace(before, after), expected);
    }
    /// <summary>Rejects unsupported stage combinations and references that would otherwise be silently ignored.</summary>
    [Theory]
    [InlineData("ShaderStageKind.Vertex", "ShaderStageKind.Compute", "stage combination")]
    [InlineData("nameof(Enabled))]", "\"Missing\")]", "unknown option")]
    [InlineData("ShaderUse(\"Contract\"", "ShaderUse(\"Missing\"", "unknown program")]
    [InlineData("ShaderUse(\"Contract\", ShaderStageKind.Fragment", "ShaderUse(\"Contract\", ShaderStageKind.Geometry", "undeclared stage")]
    public void InvalidStageDeclarationsFail(string before, string after, string expected)
    {
        AssertDiagnostic(DeclarationTests.Basic.Replace(before, after), expected);
    }
    /// <summary>Uses the same model validation for ranges, integer domains and default membership.</summary>
    [Theory]
    [InlineData("10", "", "finite")]
    [InlineData("10", ", Domain = new object[] { 1, 2 }", "rejects value")]
    [InlineData("1", ", Domain = new object[] { 1, 1 }", "duplicate")]
    [InlineData("1", ", Domain = new object[] { 1, 2 }, Minimum = 2, Maximum = 1", "inverted")]
    public void IntegerDomainsAreValidated(string value, string metadata, string expected)
    {
        string source = DeclarationTests.Basic.Replace("ShaderOption<bool>", "ShaderOption<int>")
            .Replace("false, Aliases = new[] { \"OLD_ENABLED\" }", value + metadata);
        AssertDiagnostic(source, expected);
    }
    /// <summary>Specialization IDs and structural availability are validated before code emission.</summary>
    [Theory]
    [InlineData("[ShaderUse(\"Contract\", ShaderStageKind.Fragment, nameof(Enabled), SpecializationId = -2)]", "invalid specialization")]
    [InlineData("[ShaderUse(\"Contract\", ShaderStageKind.Fragment, nameof(Enabled), SpecializationId = 1, When = \"missing\")]", "missing")]
    [InlineData("[ShaderUse(\"Contract\", ShaderStageKind.Fragment, nameof(Enabled), SpecializationId = 1, When = \"Enabled\")]", "nonstructural")]
    public void SpecializationsRejectInvalidMetadata(string use, string expected)
    {
        string source = DeclarationTests.Basic.Replace("[ShaderUse(\"Contract\", ShaderStageKind.Fragment, nameof(Enabled))]", use);
        AssertDiagnostic(source, expected);
    }
    /// <summary>Conflicting declarations cannot depend on owner discovery order.</summary>
    [Fact]
    public void DuplicateProgramAndSharedStageConflictsFail()
    {
        string other = DeclarationTests.Basic.Replace("class Shader", "class Other");
        AssertDiagnostic(DeclarationTests.Basic + other, "Duplicate program");
        other = other.Replace("\"example\"", "\"other\"").Replace("false, Aliases", "true, Aliases");
        AssertDiagnostic(DeclarationTests.Basic + other, "Conflicting shared stage");
    }
    /// <summary>Duplicate IDs diagnose the owning stage rather than failing in the driver.</summary>
    [Fact]
    public void DuplicateSpecializationIdsFail()
    {
        string source = DeclarationTests.Basic.Replace("[ShaderUse(\"Contract\", ShaderStageKind.Fragment, nameof(Enabled))]", """
            [ShaderUse("Contract", ShaderStageKind.Fragment, nameof(Enabled), SpecializationId = 1)]
            [ShaderUse("Contract", ShaderStageKind.Fragment, nameof(Other), SpecializationId = 1)]
            """).Replace("internal static partial ShaderOption<bool> Enabled { get; }", """
            internal static partial ShaderOption<bool> Enabled { get; }
            [ShaderOption("OTHER", 1)] internal static partial ShaderOption<int> Other { get; }
            """);
        AssertDiagnostic(source, "specialization ID");
    }
    /// <summary>Malformed metadata is rejected offline even though runtime implementation code is not compiled there.</summary>
    [Fact]
    public void OfflineRejectsInvalidAttributeArguments()
    {
        var output = GeneratorFixture.Generate(DeclarationTests.Basic.Replace("\"example\", 2)", "\"example\", 2, Scop = \"wrong\")"), true);
        Assert.Contains(output.Diagnostics, d => d.Id == "VGEGEN001" && d.GetMessage().Contains("Scop"));
    }
    /// <summary>Shared references must agree on their scalar type.</summary>
    [Fact]
    public void SharedReferencesRejectTypeMismatch()
    {
        string source = DeclarationTests.Basic + """
            internal static partial class Consumer
            {
                [ShaderOptionReference(typeof(Shader), nameof(Shader.Enabled))]
                internal static partial ShaderOption<int> Other { get; }
            }
            """;
        AssertDiagnostic(source, "incompatible type");
    }
    /// <summary>Assignments must include the default and complete canonical structural selections.</summary>
    [Theory]
    [InlineData("[ShaderAssignment(\"Contract\", \"ENABLED=1\")]", "omit default")]
    [InlineData("[ShaderAssignment(\"Contract\")]", "missing structural")]
    [InlineData("[ShaderAssignment(\"Contract\", \"ENABLED=0\")][ShaderAssignment(\"Contract\", \"ENABLED=0\")]", "repeats assignment")]
    public void InvalidSupportedAssignmentsFail(string attribute, string expected) =>
        AssertDiagnostic(DeclarationTests.Basic.Replace("internal static partial class Shader", attribute + "internal static partial class Shader"), expected);

    /// <summary>Asserts generator-owned error identity, source location and contextual message.</summary>
    private static void AssertDiagnostic(string source, string expected)
    {
        var result = GeneratorFixture.Generate(source);
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Contains(errors, d => d.Id == "VGEGEN001" && d.Location != Location.None && d.GetMessage().Contains(expected, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(result.Generated);
    }
    #endregion
}
