using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderBuildTool.Spirv;

/// <summary>Declares test-only shader inputs for the shared variant compiler without expanding the production catalog.</summary>
internal static class TestShaderPrograms
{
    #region Public API
    /// <summary>Groups fixture stages into valid program declarations while sharing their compiled binaries.</summary>
    internal static ShaderVariantResolver Create()
    {
        var fragment = Stage("fragment", ShaderStageKind.Fragment);
        var vertex = Stage("present", ShaderStageKind.Vertex);
        var programs = GpuShaderContracts.Registry.Programs.Values.ToList();
        foreach (string name in new[] { "absent", "present", "specialized", "conditional", "helper" })
            programs.Add(new(name, [Stage(name, ShaderStageKind.Vertex), fragment], 1));
        foreach (string name in new[] { "geometry", "geometry-triangles" })
            programs.Add(new(name, [vertex, Stage(name, ShaderStageKind.Geometry), fragment], 1));
        programs.Add(new("tessellation", [vertex, Stage("control", ShaderStageKind.TessellationControl),
            Stage("evaluation", ShaderStageKind.TessellationEvaluation), fragment], 1));
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
        return new(programs);
    }
    #endregion

    #region Private
    /// <summary>Uses existing fixture names as binary identities and leaves native specialization declarations in source.</summary>
    private static ShaderStageContract Stage(string name, ShaderStageKind kind) =>
        new("tests/clipdistance/" + name, "tests/clipdistance/" + name + ".glsl", kind, new GpuBindingContract());
    /// <summary>Declares fixed fixture interfaces whose numeric locations are authored in source.</summary>
    private static ShaderStageContract Fixture(string name, string suffix, ShaderStageKind kind)
    {
        var bindings = new GpuBindingContract();
        if (name == "temporal-debug") bindings.RegisterShaderStorageBlockBinding("Result", 0);
        if (name == "sampler-handoff") bindings.RegisterSamplerUnit("ordinaryTexture", 5);
        return new("tests/" + name + "." + suffix, "tests/" + name + "." + suffix, kind, bindings);
    }
    /// <summary>Assigns stable locations to numerical fixtures and their imported production kernels.</summary>
    private static ShaderStageContract Numerical(string name, ShaderStageKind kind = ShaderStageKind.Fragment)
    {
        string[] uniforms = name.StartsWith("relief-", StringComparison.Ordinal) || name == "eye-relative"
            ? ["vge_normalDepthTex", "metric", "surface", "outputMode", "modelViewMatrix", "vge_displacementTex", "vge_displacementRecords", "vge_twoSidedTerrain"]
            : name.StartsWith("sun-", StringComparison.Ordinal)
            ? ["elevation", "camera", "vge_atmosphereSunDraw", "vge_atmosphereSun", "vge_atmosphereDisk", "vge_sceneLinear"]
            : name == "aerial-lookup"
            ? ["displacement", "visibility", "vge_atmosphereAerialRadiance", "vge_atmosphereAerialAttenuation"]
            : ["distance", "sampleInput", "vge_displacementTex", "vge_normalDepthTex", "mvpMatrix", "modelViewMatrix",
                "projectionMatrix", "vge_tessellationFocalPixels", "vge_displacementEnabled", "vge_tessellationPixels", "vge_tessellationDistance"];
        var bindings = new GpuBindingContract();
        // Leave room for matrix columns so the fixture ABI remains independent of declaration order.
        for (int index = 0; index < uniforms.Length; index++) bindings.UniformLocations.Add(uniforms[index], index * 4);
        if (name == "aerial-lookup")
        {
            bindings.RegisterSamplerUnit("vge_atmosphereAerialRadiance", 11, required: false);
            bindings.RegisterSamplerUnit("vge_atmosphereAerialAttenuation", 12, required: false);
        }
        else if (!name.StartsWith("sun-", StringComparison.Ordinal))
        {
            bindings.RegisterSamplerUnit("vge_normalDepthTex", 0, required: false);
            bindings.RegisterSamplerUnit("vge_displacementTex", 1, required: false);
            if (name.StartsWith("relief-", StringComparison.Ordinal) || name == "eye-relative")
                bindings.RegisterSamplerUnit("vge_displacementRecords", 2, required: false);
        }
        bindings.FragmentOutputLocations.Add("result", 0);
        bindings.FragmentOutputLocations.Add("color", 0);
        bindings.VaryingLocations.Add("vge_sunDirection", 0);
        bindings.VaryingLocations.Add("vge_sunPlane", 1);
        string path = "tests/" + name + (kind == ShaderStageKind.Vertex ? ".vsh" : ".fsh");
        return new(path, path, kind, bindings);
    }
    #endregion
}
