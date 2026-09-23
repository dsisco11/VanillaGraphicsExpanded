namespace ShaderContractGenerator.Tests;

/// <summary>Exercises shared option keys, closed conditions and emitted accessor implementations.</summary>
public sealed class OptionTests
{
    #region Shared declarations
    /// <summary>Shared definitions retain aliases/defaults and supply program groups without copying metadata.</summary>
    [Fact]
    public void SharedOptionsGroupsAndConditionsCompileAndExecute()
    {
        const string source = """
            [ShaderGroup("Lighting", "lighting", nameof(Enabled), nameof(Steps))]
            internal static partial class Shared
            {
                [ShaderOption("ENABLED", false, Aliases = new[] { "OLD_ENABLED" })]
                internal static partial ShaderOption<bool> Enabled { get; }
                [ShaderOption("STEPS", 10, Minimum = 1, Maximum = 32)]
                internal static partial ShaderOption<int> Steps { get; }
            }
            [ShaderProgram("Contract", "example", 2)]
            [ShaderStage("Contract", ShaderStageKind.Vertex, "shared.vsh")]
            [ShaderStage("Contract", ShaderStageKind.Fragment, "example.fsh")]
            [ShaderAcceptGroup("Contract", typeof(Shared), "Lighting")]
            [ShaderUse("Contract", ShaderStageKind.Fragment, nameof(Enabled))]
            [ShaderUse("Contract", ShaderStageKind.Fragment, nameof(Steps), SpecializationId = 4, When = "Enabled && (Enabled || !Enabled)")]
            [ShaderFixedDefine("Contract", ShaderStageKind.Fragment, "EXTRA", 7u)]
            internal partial class Shader : Host
            {
                [ShaderOptionReference(typeof(Shared), nameof(Shared.Enabled))]
                public partial bool Enabled { get; set; }
                [ShaderOptionReference(typeof(Shared), nameof(Shared.Steps))]
                public partial int Steps { get; set; }
                internal override GpuShaderContract Declaration => Contract;
            }
            internal abstract class Host
            {
                private ShaderSettings? selected;
                internal abstract GpuShaderContract Declaration { get; }
                internal T GetShaderOption<T>(ShaderOption<T> key) where T : struct => ShaderOptionAccess.Get(selected ??= new(Declaration), key);
                internal void SetShaderOptions(System.Action<ShaderSettingsEditor> configure) { var editor = new ShaderSettingsEditor(selected ?? new(Declaration)); configure(editor); selected = editor.Complete(); }
            }
            public static class Proof
            {
                public static string Run()
                {
                    var shader = new Shader { Steps = 12, Enabled = true };
                    var stage = Shader.Contract.Stages[1];
                    bool rejected = false;
                    try { shader.Steps = 0; } catch (System.ArgumentException) { rejected = true; }
                    return shader.Enabled + ":" + shader.Steps + ":" + rejected + ":" + stage.Specializations[0].Id + ":" +
                        object.ReferenceEquals(Shared.Steps, Shader.StepsOption) + ":" + Shader.Contract.Groups[0].Identity + ":" +
                        stage.Specializations[0].Condition!.Evaluate(new ShaderSettings(Shader.Contract).Values) + ":" +
                        stage.Specializations[0].Condition!.Evaluate(new ShaderSettings(Shader.Contract).With(Shader.EnabledOption, true).Values);
                }
            }
            """;
        Assert.Equal("True:12:True:4:True:lighting:False:True", GeneratorFixture.Generate(source).Run());
        GeneratorFixture.Generate(source, offline: true).Compile();
    }
    /// <summary>Enum keys retain their CLR type while sharing exact integer scalar encoding.</summary>
    [Fact]
    public void EnumDomainsPreserveTypedKeys()
    {
        const string source = """
            internal enum Mode : uint { A = 1, B = 3 }
            [ShaderProgram("Contract", "example", 2)]
            [ShaderStage("Contract", ShaderStageKind.Compute, "example.csh")]
            [ShaderUse("Contract", ShaderStageKind.Compute, nameof(Mode))]
            internal static partial class Shader
            {
                [ShaderOption("MODE", Example.Mode.A, Domain = new object[] { Example.Mode.A, Example.Mode.B })]
                internal static partial ShaderOption<Mode> Mode { get; }
            }
            public static class Proof { public static string Run() => Shader.Mode.Default.Canonical + ":" + Shader.Contract.Assignments.Count; }
            """;
        Assert.Equal("1:2", GeneratorFixture.Generate(source).Run());
        GeneratorFixture.Generate(source, offline: true).Compile();
    }
    #endregion
}
