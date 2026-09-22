namespace ShaderContractGenerator.Tests;

/// <summary>Checks typed literal and enum comparisons, including reused shared option declarations.</summary>
public sealed class ConditionScalarTests
{
    #region Typed constants
    /// <summary>Exact typed constants retain their domains and evaluate identically offline and at runtime.</summary>
    [Theory]
    [InlineData("int", "-2147483648", "new object[] { -2147483648, 2 }", "Key == -2147483648")]
    [InlineData("uint", "4294967295u", "new object[] { 0u, 4294967295u }", "Key == 4294967295u")]
    [InlineData("uint", "1u", "new object[] { 1u, 2u }", "Key == 0x1u")]
    [InlineData("uint", "1u", "new object[] { 1u, 2u }", "Key == 0b0_1u")]
    [InlineData("Choice", "Choice.On", "new object[] { Choice.Off, Choice.On }", "Key == global:: Example . @Choice . @On")]
    [InlineData("Choice", "Choice.On", "new object[] { Choice.Off, Choice.On }", "Key == Choice.On")]
    [InlineData("Choice", "Choice.On", "new object[] { Choice.Off, Choice.On }", "Key == Example.Choice.On")]
    [InlineData("Choice", "Choice.On", "new object[] { Choice.Off, Choice.On }", "Key == global::Example.Choice.On")]
    public void TypedEqualityAndSharedReferences(string type, string fallback, string domain, string expression)
    {
        string source = Source(type, fallback, domain, expression);
        const string proof = "public static class Proof { public static string Run() => Shader.Contract.Stages[0].Specializations[0].Condition!.Evaluate(new ShaderSettings(Shader.Contract).Values).ToString(); }";
        foreach (bool offline in new[] { false, true }) Assert.Equal("True", ConditionTests.Execute(source, proof, offline));
    }

    /// <summary>Rejects numeric coercion, arithmetic, out-of-domain values and arbitrary enum constants.</summary>
    [Theory]
    [InlineData("int", "0", "new object[] { 0, 1 }", "Key")]
    [InlineData("int", "0", "new object[] { 0, 1 }", "Key == 0u")]
    [InlineData("int", "0", "new object[] { 0, 1 }", "Key == 0.0")]
    [InlineData("int", "0", "new object[] { 0, 1 }", "Key == 2")]
    [InlineData("int", "0", "new object[] { 0, 1 }", "Key == 1 + 0")]
    [InlineData("int", "0", "new object[] { 0, 1 }", "Key == int.MaxValue")]
    [InlineData("uint", "0u", "new object[] { 0u, 1u }", "Key == 0")]
    [InlineData("uint", "0u", "new object[] { 0u, 1u }", "Key == -1u")]
    [InlineData("Choice", "Choice.On", "new object[] { Choice.Off, Choice.On }", "Key == Other.On")]
    [InlineData("Choice", "Choice.On", "new object[] { Choice.Off, Choice.On }", "Key == Choice.Missing")]
    [InlineData("Choice", "Choice.On", "new object[] { Choice.Off, Choice.On }", "Key == (Choice)1")]
    [InlineData("Choice", "Choice.On", "new object[] { Choice.Off, Choice.On }", "Key == 1u")]
    public void InvalidTypedConstantsFail(string type, string fallback, string domain, string expression)
    {
        foreach (bool offline in new[] { false, true }) {
            var diagnostic = Assert.Single(GeneratorFixture.Generate(Source(type, fallback, domain, expression), offline).Diagnostics);
            Assert.Equal("VGEGEN001", diagnostic.Id);
            Assert.Contains("expression '" + expression + "'", diagnostic.GetMessage());
        }
    }
    #endregion

    #region Fixture declarations
    /// <summary>Uses an owner-local reference to a shared typed key so equality resolves the underlying scalar or enum.</summary>
    private static string Source(string type, string fallback, string domain, string expression) => $$"""
        internal enum Choice : uint { Off = 0, On = 1 }
        internal enum Other : uint { On = 1 }
        internal static partial class Shared {
            [ShaderOption("MODE", {{fallback}}, Domain = {{domain}})]
            internal static partial ShaderOption<{{type}}> Mode { get; }
        }
        [ShaderProgram("Contract", "typed", 2)]
        [ShaderStage("Contract", ShaderStageKind.Compute, "typed.csh")]
        [ShaderUse("Contract", ShaderStageKind.Compute, nameof(Key))]
        [ShaderUse("Contract", ShaderStageKind.Compute, nameof(Value), SpecializationId = 3, When = "{{expression}}")]
        internal static partial class Shader {
            [ShaderOptionReference(typeof(Shared), nameof(Shared.Mode))]
            internal static partial ShaderOption<{{type}}> Key { get; }
            [ShaderOption("VALUE", 10)]
            internal static partial ShaderOption<int> Value { get; }
        }
        """;
    #endregion
}

