using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderBuildTool.Spirv;

/// <summary>Declares test-only shader inputs for the shared variant compiler without expanding the production catalog.</summary>
internal static class TestShaderPrograms
{
    #region Public API
    /// <summary>Groups fixture stages into valid program declarations while sharing their compiled binaries.</summary>
    internal static ShaderVariantResolver Create()
    {
        var programs = GpuShaderContracts.Registry.Programs.Values.ToList();
        foreach (string name in new[] { "terrain-capture-opaque", "terrain-capture-topsoil" })
            programs.Add(new("tests/" + name, [Fixture("complete-state", "vsh", ShaderStageKind.Vertex), Fixture(name, "fsh", ShaderStageKind.Fragment)], 1));
        foreach (string name in new[] { "complete-state", "rasterizer", "first-person" })
            programs.Add(new("tests/" + name, [Fixture(name, "vsh", ShaderStageKind.Vertex), Fixture(name, "fsh", ShaderStageKind.Fragment)], 1));
        programs.Add(new("tests/deletion", [Fixture("deletion", "vsh", ShaderStageKind.Vertex), Fixture("complete-state", "fsh", ShaderStageKind.Fragment)], 1));
        programs.Add(new("tests/temporal-debug", [Fixture("temporal-debug", "csh", ShaderStageKind.Compute)], 1));
        foreach (string name in new[] { "aerial-lookup", "displacement-metric", "displacement-height", "sun-segment", "relief-on", "relief-off" })
            programs.Add(new("tests/" + name, [Fixture("complete-state", "vsh", ShaderStageKind.Vertex), Numerical(name)], 1));
        foreach (string name in new[] { "particle-draw", "particle-integration", "sampler-handoff" })
            programs.Add(new("tests/" + name, [Fixture("complete-state", "vsh", ShaderStageKind.Vertex), Fixture(name, "fsh", ShaderStageKind.Fragment)], 1));
        foreach (string mode in new[] { "display", "linear" })
            programs.Add(new("tests/sun-raster-" + mode, [Numerical("sun-raster", ShaderStageKind.Vertex), Numerical("sun-raster-" + mode)], 1));
        programs.Add(new("tests/eye-relative", [Numerical("eye-relative", ShaderStageKind.Vertex), Numerical("eye-relative")], 1));
        programs.Add(new("tests/normal-input", [Fixture("normal-input", "vsh", ShaderStageKind.Vertex), Fixture("normal-input", "fsh", ShaderStageKind.Fragment)], 1));
        foreach (string name in new[] { "foliage-transmission", "liquid-interface" })
            programs.Add(new("tests/" + name, [Fixture("numerical-quad", "vsh", ShaderStageKind.Vertex), Fixture(name, "fsh", ShaderStageKind.Fragment)], 1));
        return new(programs);
    }
    #endregion

    #region Private
    /// <summary>Declares fixed fixture resource interfaces and numeric input blocks.</summary>
    private static ShaderStageContract Fixture(string name, string suffix, ShaderStageKind kind)
    {
        var bindings = new GpuBindingContract();
        if (name == "temporal-debug") bindings.RegisterShaderStorageBlockBinding("Result", 0);
        string? block = name switch { "temporal-debug" => "TemporalDebugInputs", "rasterizer" => "RasterizerInputs", "particle-draw" => "ParticleDrawInputs", "terrain-capture-opaque" or "terrain-capture-topsoil" => "TerrainCaptureInputs", "normal-input" or "foliage-transmission" or "liquid-interface" => "TestInputs", _ => null };
        if (block != null) bindings.RegisterUniformBlockBinding(block, 28);
        if (name == "sampler-handoff") bindings.RegisterSamplerUnit("ordinaryTexture", 5);
        return new("tests/" + name + "." + suffix, "tests/" + name + "." + suffix, kind, bindings);
    }
    /// <summary>Assigns numeric input blocks and opaque resource interfaces to numerical fixtures.</summary>
    private static ShaderStageContract Numerical(string name, ShaderStageKind kind = ShaderStageKind.Fragment)
    {
        var bindings = new GpuBindingContract();
        // Preserve opaque resource locations while numeric controls live entirely in blocks.
        if (name.StartsWith("relief-", StringComparison.Ordinal) || name == "eye-relative")
        {
            bindings.UniformLocations.Add("vge_normalDepthTex", 0);
            bindings.UniformLocations.Add("vge_displacementTex", 20);
            bindings.UniformLocations.Add("vge_displacementRecords", 24);
        }
        else if (name == "aerial-lookup")
        {
            bindings.UniformLocations.Add("vge_atmosphereAerialRadiance", 8);
            bindings.UniformLocations.Add("vge_atmosphereAerialAttenuation", 12);
            bindings.UniformLocations.Add("vge_lightShaftOcclusion", 13);
        }
        else if (!name.StartsWith("sun-", StringComparison.Ordinal))
        {
            bindings.UniformLocations.Add("vge_displacementTex", 8);
            bindings.UniformLocations.Add("vge_normalDepthTex", 12);
        }
        if (name == "aerial-lookup")
        {
            bindings.RegisterSamplerUnit("vge_atmosphereAerialRadiance", 11, required: false);
            bindings.RegisterSamplerUnit("vge_atmosphereAerialAttenuation", 12, required: false);
            bindings.RegisterSamplerUnit("vge_lightShaftOcclusion", 13, required: false);
        }
        else if (!name.StartsWith("sun-", StringComparison.Ordinal))
        {
            bindings.RegisterSamplerUnit("vge_normalDepthTex", 0, required: false);
            bindings.RegisterSamplerUnit("vge_displacementTex", 1, required: false);
            if (name.StartsWith("relief-", StringComparison.Ordinal) || name == "eye-relative")
                bindings.RegisterSamplerUnit("vge_displacementRecords", 2, required: false);
        }
        string block = name switch
        {
            "eye-relative" => "EyeInputs",
            "aerial-lookup" => "AerialLookupInputs",
            "sun-segment" => "SunSegmentInputs",
            _ when name.StartsWith("sun-raster", StringComparison.Ordinal) => "SunInputs",
            _ when name.StartsWith("relief-", StringComparison.Ordinal) => "ReliefInputs",
            _ => "DisplacementInputs"
        };
        bindings.RegisterUniformBlockBinding(block, 28);
        if (name is "displacement-metric" or "displacement-height" or "sun-raster" or "eye-relative" || name.StartsWith("relief-", StringComparison.Ordinal))
            bindings.RegisterUniformBlockBinding("VgeFrameUBO", 12, required: false);
        bindings.FragmentOutputLocations.Add("result", 0);
        bindings.FragmentOutputLocations.Add("color", 0);
        bindings.VaryingLocations.Add("vge_sunDirection", 0);
        bindings.VaryingLocations.Add("vge_sunPlane", 1);
        string path = "tests/" + name + (kind == ShaderStageKind.Vertex ? ".vsh" : ".fsh");
        return new(path, path, kind, bindings);
    }
    #endregion
}
