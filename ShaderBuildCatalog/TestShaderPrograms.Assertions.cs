using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderBuildTool.Spirv;

/// <summary>Declares the fixed interfaces and finite numeric interpretations of GPU observation assets.</summary>
internal static partial class TestShaderPrograms
{
    #region Private
    /// <summary>Adds checker, projection and conversion inputs to the existing test asset build.</summary>
    private static void AddAssertions(List<GpuShaderContract> programs)
    {
        for (int kind = 0; kind < 3; kind++)
        {
            for (int expression = 0; expression < 4; expression++)
                programs.Add(Assertion("check", kind, expression));
            programs.Add(Assertion("project", kind, 0));
        }
        programs.Add(Assertion("predicate", 0, 0));
        programs.Add(Assertion("layer", 0, 0));
    }

    /// <summary>Freezes one precompiled input and its authored binding interface, without native preparation.</summary>
    private static GpuShaderContract Assertion(string family, int kind, int expression)
    {
        var bindings = new GpuBindingContract();
        bindings.RegisterUniformBlockBinding("AssertionInputs", 28);
        bool checker = family is "check" or "predicate";
        string[] samplers = checker
            ? ["actualImage", "historyImage", "expectedImage", "additionalImage", "fourthImage"]
            : ["sourceImage", "referenceImage"];
        for (int unit = 0; unit < samplers.Length; unit++) bindings.RegisterSamplerUnit(samplers[unit], unit, required: false);
        string[] images = checker ? ["resultImage", "histogram"]
            : family == "layer" ? ["destination"] : ["destinationImage"];
        for (int unit = 0; unit < images.Length; unit++) bindings.RegisterImageUnit(images[unit], unit, required: false);
        string identity = $"tests/assertion-{family}-{kind}-{expression}";
        string source = family == "predicate" ? "check" : family;
        var stage = new ShaderStageContract(identity + ".csh", $"tests/assertion-{source}.csh",
            ShaderStageKind.Compute, bindings, fixedDefines: new Dictionary<string, ShaderScalar>
            { ["KIND"] = ShaderScalar.From(kind), ["STATISTIC_EXPRESSION"] = ShaderScalar.From(expression),
                ["AUTHORED_PROGRAM"] = ShaderScalar.From(family == "predicate" ? 1 : 0) });
        return new(identity, [stage], 1);
    }
    #endregion
}
