using Microsoft.CodeAnalysis.CSharp;

namespace ShaderContractGenerator.Tests;

/// <summary>Exercises inline availability authoring through compiled runtime and offline declarations.</summary>
public sealed class ConditionTests
{
    private const string BooleanOwner = """
        [ShaderProgram("Contract", "condition", 8)]
        [ShaderStage("Contract", ShaderStageKind.Compute, "condition.csh")]
        [ShaderUse("Contract", ShaderStageKind.Compute, nameof(A))]
        [ShaderUse("Contract", ShaderStageKind.Compute, nameof(B))]
        [ShaderUse("Contract", ShaderStageKind.Compute, nameof(C))]
        [ShaderUse("Contract", ShaderStageKind.Compute, nameof(Value), SpecializationId = 9, When = "EXPRESSION")]
        internal static partial class Shader
        {
            [ShaderOption("GLSL_A", false, Aliases = new[] { "OLD_A" })]
            internal static partial ShaderOption<bool> A { get; }
            [ShaderOption("GLSL_B", false)]
            internal static partial ShaderOption<bool> B { get; }
            [ShaderOption("GLSL_C", false)]
            internal static partial ShaderOption<bool> C { get; }
            [ShaderOption("VALUE", 10)]
            internal static partial ShaderOption<int> Value { get; }
        }
        """;

    #region Boolean grammar
    /// <summary>Truth tables distinguish precedence, grouping, nested negation and exact Boolean equality.</summary>
    [Theory]
    [InlineData("A || B && C", "01010111")]
    [InlineData("(A || B) && C", "00000111")]
    [InlineData("!!!A", "10101010")]
    [InlineData("A == false || B == true", "10111011")]
    [InlineData("!(A == false)", "01010101")]
    [InlineData("true", "11111111")]
    [InlineData("false", "00000000")]
    [InlineData("!(A && (!B || C))", "10111010")]
    public void BooleanExpressionsPreserveTruthTables(string expression, string expected)
    {
        const string proof = """
            public static class Proof { public static string Run() {
                string result = "";
                for (int i = 0; i < 8; i++) {
                    var values = new ShaderSettings(Shader.Contract).With(Shader.A, (i & 1) != 0)
                        .With(Shader.B, (i & 2) != 0).With(Shader.C, (i & 4) != 0).Values;
                    result += Shader.Contract.Stages[0].Specializations[0].Condition!.Evaluate(values) ? "1" : "0";
                }
                return result;
            } }
            """;
        var runtime = GeneratorFixture.Generate(BooleanOwner.Replace("EXPRESSION", expression));
        var offlineOutput = GeneratorFixture.Generate(BooleanOwner.Replace("EXPRESSION", expression), true);
        Assert.Equal(runtime.Generated.Select(s => s.Replace("static partial ShaderOption", "static ShaderOption")), offlineOutput.Generated);
        foreach (bool offline in new[] { false, true })
            Assert.Equal(expected, Execute(BooleanOwner.Replace("EXPRESSION", expression), proof, offline));
    }

    /// <summary>Only omitted/null availability is unconditional; empty input is rejected elsewhere.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(", When = null")]
    public void OmittedAndNullConditionsAreUnconditional(string clause)
    {
        string source = BooleanOwner.Replace(", When = \"EXPRESSION\"", clause);
        const string proof = "public static class Proof { public static string Run() => (Shader.Contract.Stages[0].Specializations[0].Condition == null).ToString(); }";
        foreach (bool offline in new[] { false, true }) Assert.Equal("True", Execute(source, proof, offline));
    }
    #endregion

    #region Invalid expressions
    /// <summary>Invalid syntax and names fail at the use attribute in both compiler modes, including unreachable operands.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A &&")]
    [InlineData("(A")]
    [InlineData("A B")]
    [InlineData("A; true")]
    [InlineData("A != false")]
    [InlineData("A & B")]
    [InlineData("A ? B : C")]
    [InlineData("A.Equals(true)")]
    [InlineData("Shader.A")]
    [InlineData("true == A")]
    [InlineData("!A == false")]
    [InlineData("A == B")]
    [InlineData("A == 1")]
    [InlineData("Missing")]
    [InlineData("GLSL_A")]
    [InlineData("OLD_A")]
    [InlineData("AOption")]
    [InlineData("Value == 10")]
    [InlineData("true || Missing")]
    [InlineData("false && Missing")]
    public void UnsupportedConditionsHavePreciseDiagnostics(string expression)
    {
        AssertInvalid(BooleanOwner.Replace("EXPRESSION", expression), expression);
    }

    /// <summary>Property renames invalidate stale strings, while another stage's structural membership is insufficient.</summary>
    [Fact]
    public void RenamedAndOtherStagePropertiesFail()
    {
        AssertInvalid(BooleanOwner.Replace("EXPRESSION", "A").Replace("nameof(A)", "nameof(Renamed)")
            .Replace("ShaderOption<bool> A {", "ShaderOption<bool> Renamed {"), "A");
        string otherStage = BooleanOwner.Replace("EXPRESSION", "A")
            .Replace("ShaderStageKind.Compute", "ShaderStageKind.Fragment")
            .Replace("[ShaderStage(\"Contract\", ShaderStageKind.Fragment", "[ShaderStage(\"Contract\", ShaderStageKind.Vertex, \"condition.vsh\")]\n[ShaderStage(\"Contract\", ShaderStageKind.Fragment")
            .Replace("ShaderStageKind.Fragment, nameof(A)", "ShaderStageKind.Vertex, nameof(A)");
        AssertInvalid(otherStage, "A");
    }
    #endregion

    #region Fixture execution
    /// <summary>Adds a test-only observation after generation so offline mode cannot strip the proof method.</summary>
    internal static string Execute(string source, string proof, bool offline)
    {
        var result = GeneratorFixture.Generate(source, offline);
        var tree = CSharpSyntaxTree.ParseText(GeneratorFixture.Prelude + proof, new CSharpParseOptions(LanguageVersion.CSharp13));
        return (result with { Compilation = result.Compilation.AddSyntaxTrees(tree) }).Run();
    }

    /// <summary>Checks diagnostic identity, owner/stage/expression context, and the exact offending use attribute span.</summary>
    private static void AssertInvalid(string source, string expression)
    {
        foreach (bool offline in new[] { false, true }) {
            var result = GeneratorFixture.Generate(source, offline);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("VGEGEN001", diagnostic.Id);
            Assert.Contains("Shader", diagnostic.GetMessage());
            Assert.Contains("condition.csh", diagnostic.GetMessage());
            Assert.Contains("expression '" + expression + "'", diagnostic.GetMessage());
            Assert.EndsWith("Owner.cs", diagnostic.Location.GetLineSpan().Path);
            string offending = (GeneratorFixture.Prelude + source).Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);
            Assert.StartsWith("ShaderUse(", offending);
            Assert.Contains("When =", offending);
        }
    }
    #endregion
}



