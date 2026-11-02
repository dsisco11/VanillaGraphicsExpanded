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
            foreach (string family in new[] { "compare", "inspection", "ranks" }) programs.Add(Assertion(family, kind, 0));
            foreach (int expression in new[] { 0, 2, 3 }) programs.Add(Assertion("statistics", kind, expression));
            programs.Add(Assertion("projection-ao", kind, 0));
            programs.Add(Assertion("projection-observation", kind, 0));
            programs.Add(Assertion("projection-atmosphere", kind, 0));
            programs.Add(Assertion("projection-cloud", kind, 0));
            programs.Add(Assertion("projection-material", kind, 0));
            programs.Add(Assertion("projection-surface", kind, 0));
            programs.Add(Assertion("projection-lighting", kind, 0));
        }
        programs.Add(Assertion("predicate-ao", 0, 0, 27));
        programs.Add(Assertion("predicate-observation", 0, 0, 31));
        programs.Add(Assertion("predicate-atmosphere", 0, 0, 27));
        programs.Add(Assertion("predicate-material", 0, 0, 27));
        programs.Add(Assertion("predicate-cloud", 0, 0, 27));
        programs.Add(Assertion("predicate-lighting", 0, 0, 27));
        programs.Add(Assertion("predicate-surface", 0, 0, 27));
        programs.Add(Assertion("predicate-water", 0, 0, 27));
        programs.Add(Assertion("predicate-geometry", 0, 0, 11));
        programs.Add(Assertion("statistics-poisson", 0, 0));
        programs.Add(Assertion("layer", 0, 0));
    }

    /// <summary>Freezes one precompiled input and its authored binding interface, without native preparation.</summary>
    private static GpuShaderContract Assertion(string family, int kind, int expression, int predicateInputs = 0)
    {
        var bindings = new GpuBindingContract();
        bindings.RegisterUniformBlockBinding("AssertionInputs", 28);
        bool checker = family is "compare" or "inspection" or "ranks" or "statistics" || family.StartsWith("predicate-") || family == "statistics-poisson";
        string[] samplers = checker
            ? ["actualImage", "historyImage", "expectedImage", "additionalImage", "fourthImage"]
            : family == "layer" ? ["sourceImage"] : ["sourceImage", "referenceImage"];
        for (int unit = 0; unit < samplers.Length; unit++) bindings.RegisterSamplerUnit(samplers[unit], unit, required: false);
        string[] images = checker ? ["resultImage", "histogram"]
            : family == "layer" ? ["destination"] : ["destinationImage"];
        for (int unit = 0; unit < images.Length; unit++) bindings.RegisterImageUnit(images[unit], unit, required: false);
        string identity = $"tests/assertion-{family}-{kind}-{expression}";
        string source = family == "statistics-poisson" ? "observe-poisson-statistics"
            : family.StartsWith("predicate-") ? "observe-" + family[10..] + "-predicate"
            : family.StartsWith("projection-") ? "observe-" + family[11..] + "-projection"
            : family == "layer" ? "assertion-layer" : "assertion-check";
        var stage = new ShaderStageContract(identity + ".csh", $"tests/{source}.csh",
            ShaderStageKind.Compute, bindings, fixedDefines: new Dictionary<string, ShaderScalar>
            { ["KIND"] = ShaderScalar.From(kind), ["STATISTIC_EXPRESSION"] = ShaderScalar.From(expression),
                ["AUTHORED_PROGRAM"] = ShaderScalar.From(family.StartsWith("predicate-") ? 1 : 0),
                ["AUTHORED_STATISTIC"] = ShaderScalar.From(family == "statistics-poisson" ? 1 : 0),
                ["PREDICATE_INPUTS"] = ShaderScalar.From(predicateInputs),
                ["OBSERVATION_FAMILY"] = ShaderScalar.From(family.StartsWith("predicate-") ? 4
                    : family.StartsWith("statistics") ? 3 : family == "ranks" ? 2 : family == "inspection" ? 1 : 0) });
        return new(identity, [stage], 1);
    }
    #endregion
}
