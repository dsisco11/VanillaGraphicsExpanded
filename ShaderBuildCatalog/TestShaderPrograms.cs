using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering;

namespace ShaderBuildTool.Spirv;

/// <summary>Declares test-only shader inputs for the shared variant compiler without expanding the production catalog.</summary>
internal static partial class TestShaderPrograms
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
        // Shadow subdivision runs before world-camera publication, so its fixture contract deliberately omits VgeFrameUBO.
        var shadowDisplacement = new GpuBindingContract();
        shadowDisplacement.RegisterUniformBlockBinding("DisplacementInputs", 28);
        shadowDisplacement.FragmentOutputLocations.Add("result", 0);
        programs.Add(new("tests/displacement-shadow",
            [Fixture("numerical-quad", "vsh", ShaderStageKind.Vertex),
             new("tests/displacement-shadow.fsh", "tests/displacement-shadow.fsh", ShaderStageKind.Fragment, shadowDisplacement)], 1));
        foreach (string name in new[] { "particle-draw", "particle-integration", "sampler-handoff", "facing-state" })
            programs.Add(new("tests/" + name, [Fixture("complete-state", "vsh", ShaderStageKind.Vertex), Fixture(name, "fsh", ShaderStageKind.Fragment)], 1));
        programs.Add(new("tests/eye-relative", [Numerical("eye-relative", ShaderStageKind.Vertex), Numerical("eye-relative")], 1));
        programs.Add(new("tests/normal-input", [Fixture("normal-input", "vsh", ShaderStageKind.Vertex), Fixture("normal-input", "fsh", ShaderStageKind.Fragment)], 1));
        foreach (string name in new[] { "foliage-transmission", "liquid-interface" })
            programs.Add(new("tests/" + name, [Fixture("numerical-quad", "vsh", ShaderStageKind.Vertex), Fixture(name, "fsh", ShaderStageKind.Fragment)], 1));
        var solarBrdfInputs = new GpuBindingContract();
        solarBrdfInputs.RegisterUniformBlockBinding("SolarBrdfInputs", 28);
        solarBrdfInputs.FragmentOutputLocations.Add("result", 0);
        programs.Add(new("tests/pbr-solar-brdf", [Fixture("numerical-quad", "vsh", ShaderStageKind.Vertex),
            new ShaderStageContract("tests/pbr-solar-brdf.fsh", "tests/pbr-solar-brdf.fsh",
                ShaderStageKind.Fragment, solarBrdfInputs)], 1));
        var localExposureInputs = new GpuBindingContract();
        localExposureInputs.RegisterUniformBlockBinding("LocalExposureProbeInputs", 28);
        localExposureInputs.RegisterSamplerUnit("sourceImage", 0);
        localExposureInputs.RegisterSamplerUnit("baseImage", 1);
        localExposureInputs.RegisterSamplerUnit("gradingLookup", 2, required: false);
        localExposureInputs.FragmentOutputLocations.Add("result", 0);
        // Production compute programs expose the log/filter stages, but no production stage exposes
        // guided sample/correction tuples or the undithered grading lookup asserted by this suite.
        var postprocessVertex = GpuShaderContracts.Registry.Programs["pbr_final"].Stages[0];
        programs.Add(new("tests/local-exposure-prototype", [postprocessVertex,
            new ShaderStageContract("tests/local-exposure-prototype.fsh", "tests/local-exposure-prototype.fsh",
                ShaderStageKind.Fragment, localExposureInputs)], 1));
        var microfacetInputs = new GpuBindingContract();
        microfacetInputs.RegisterUniformBlockBinding("MicrofacetEnergyInputs", 28);
        microfacetInputs.RegisterSamplerUnit("vge_microfacetEnergy", 32);
        microfacetInputs.RegisterSamplerUnit("lookupCoordinates", 0, required: false);
        microfacetInputs.FragmentOutputLocations.Add("result", 0);
        programs.Add(new("tests/microfacet-energy", [Fixture("numerical-quad", "vsh", ShaderStageKind.Vertex),
            new("tests/microfacet-energy.fsh", "tests/microfacet-energy.fsh", ShaderStageKind.Fragment, microfacetInputs)], 1));
        foreach (bool compensated in new[] { false, true })
        {
            var measurementInputs = new GpuBindingContract();
            measurementInputs.RegisterUniformBlockBinding("MicrofacetMeasurementInputs", 28);
            if (compensated) measurementInputs.RegisterSamplerUnit("vge_microfacetEnergy", 32);
            measurementInputs.FragmentOutputLocations.Add("result", 0);
            string identity = compensated ? "tests/microfacet-measurement-compensated" : "tests/microfacet-measurement-single";
            programs.Add(new(identity, [Fixture("numerical-quad", "vsh", ShaderStageKind.Vertex),
                new ShaderStageContract(identity + ".fsh", "tests/microfacet-measurement.fsh", ShaderStageKind.Fragment,
                    measurementInputs, fixedDefines: new Dictionary<string, ShaderScalar>
                    { ["VGE_MICROFACET_MEASUREMENT_COMPENSATED"] = ShaderScalar.From(compensated ? 1 : 0) })], 1));
        }
        var grazingProbe = new GpuBindingContract();
        grazingProbe.RegisterUniformBlockBinding("GrazingProbeInputs", 28);
        grazingProbe.RegisterShaderStorageBlockBinding("FrozenFrameLighting", 4);
        grazingProbe.RegisterSamplerUnit("vge_microfacetEnergy", 32);
        grazingProbe.FragmentOutputLocations.Add("result", 0);
        // These raster probes share the framework's production fullscreen-triangle vertex stage and attribute layout.
        programs.Add(new("tests/frame-lighting-grazing", [postprocessVertex,
            new("tests/frame-lighting-grazing.fsh", "tests/frame-lighting-grazing.fsh", ShaderStageKind.Fragment, grazingProbe)], 1));
        var oitWriter = new GpuBindingContract();
        oitWriter.RegisterUniformBlockBinding("OitOwnedWriterInputs", 28);
        foreach (var output in new[] { ("outOitRevealBins", 0), ("outRevealage", 1), ("outOitGlow", 2),
                     ("outOitAccumulation0", 3), ("outOitAccumulation1", 4), ("outOitAccumulation2", 5) })
            oitWriter.FragmentOutputLocations.Add(output.Item1, output.Item2);
        programs.Add(new("tests/oit-owned-writer", [postprocessVertex,
            new("tests/oit-owned-writer.fsh", "tests/oit-owned-writer.fsh", ShaderStageKind.Fragment, oitWriter)], 1));
        var photometricProbe = new GpuBindingContract();
        photometricProbe.RegisterUniformBlockBinding("VgeFrameUBO", 12);
        photometricProbe.RegisterUniformBlockBinding("PhotometricProbeInputs", 28);
        photometricProbe.RegisterShaderStorageBlockBinding("AtmosphereParameters", 0);
        photometricProbe.RegisterShaderStorageBlockBinding("FrozenFrameLighting", 4);
        photometricProbe.FragmentOutputLocations.Add("result", 0);
        programs.Add(new("tests/photometric-helpers", [Fixture("complete-state", "vsh", ShaderStageKind.Vertex),
            new("tests/photometric-helpers.fsh", "tests/photometric-helpers.fsh", ShaderStageKind.Fragment, photometricProbe)], 1));
        foreach (bool lumon in new[] { true, false })
        {
            var bindings = new GpuBindingContract();
            bindings.RegisterUniformBlockBinding("VgeFrameUBO", 12);
            bindings.RegisterUniformBlockBinding("VgeLightsUBO", GpuBindingRegistry.Ubo.Lights);
            bindings.RegisterUniformBlockBinding("TestInputs", 28);
            bindings.RegisterShaderStorageBlockBinding("FrozenFrameLighting", 4);
            bindings.RegisterSamplerUnit("shadowMapFar", 0);
            bindings.RegisterSamplerUnit("vge_atmosphereAerialRadiance", 11);
            bindings.RegisterSamplerUnit("vge_atmosphereAerialAttenuation", 12);
            bindings.RegisterSamplerUnit("vge_lightShaftOcclusion", 13);
            bindings.RegisterSamplerUnit("vge_microfacetEnergy", 32);
            bindings.FragmentOutputLocations.Add("outColor", 0);
            string identity = lumon ? "tests/pbr-forward-surface" : "tests/pbr-forward-surface-legacy-environment";
            var stage = new ShaderStageContract(identity + ".fsh", "tests/pbr-forward-surface.fsh",
                ShaderStageKind.Fragment, bindings, fixedDefines: new Dictionary<string, ShaderScalar>
                { ["VGE_PBR_FORWARD_LUMON"] = ShaderScalar.From(lumon ? 1 : 0) });
            programs.Add(new(identity, [Fixture("complete-state", "vsh", ShaderStageKind.Vertex), stage], 1));
        }
        var integration=new GpuBindingContract();
        integration.RegisterUniformBlockBinding("AoIntegralTestInputs",28);
        integration.FragmentOutputLocations.Add("result",0);
        programs.Add(new("tests/ao-integral",[Fixture("complete-state","vsh",ShaderStageKind.Vertex),
            new ShaderStageContract("tests/ao-integral.fsh","tests/ao-integral.fsh",ShaderStageKind.Fragment,integration)],1));
        // Freeze the previous horizon executable for equivalent-work image and timing comparisons.
        var ao = GpuShaderContracts.Registry.Programs["pbr_post_ssao"];
        var frozenAo = new GpuBindingContract();
        frozenAo.RegisterUniformBlockBinding("VgeFrameUBO", 12);
        frozenAo.RegisterUniformBlockBinding("AmbientOcclusionInputs", 28);
        frozenAo.RegisterSamplerUnit("depthImage", 0);
        frozenAo.RegisterSamplerUnit("surfaceImage", 1);
        frozenAo.RegisterSamplerUnit("depthHierarchy", 2);
        frozenAo.FragmentOutputLocations.Add("outOcclusion", 0);
        programs.Add(new("tests/ao-horizon-reference", [ao.Stages[0],
            new ShaderStageContract("tests/ao-horizon-reference.fsh", "tests/ao-horizon-reference.fsh",
                ShaderStageKind.Fragment, frozenAo)], 1));
        foreach (bool adaptation in new[] { false, true })
        {
            var bindings = new GpuBindingContract();
            bindings.RegisterUniformBlockBinding("CameraExposureInputs", 28);
            if (adaptation) { bindings.RegisterUniformBlockBinding("VgeFrameUBO", 12); bindings.RegisterSamplerUnit("histogram", 0); bindings.RegisterSamplerUnit("previousExposure", 1); bindings.FragmentOutputLocations.Add("exposureEV", 0); }
            else { bindings.RegisterSamplerUnit("sceneRadiance", 0); bindings.FragmentOutputLocations.Add("histogram", 0); }
            string name = adaptation ? "raster-camera-adapt" : "raster-camera-histogram";
            programs.Add(new("tests/" + name, [new("tests/raster-camera.vsh", "tests/raster-camera.vsh", ShaderStageKind.Vertex, new()), new("tests/" + name + ".fsh", "tests/" + name + ".fsh", ShaderStageKind.Fragment, bindings)], 1));
        }
        AddAssertions(programs);
        return new(programs);
    }
    #endregion

    #region Private
    /// <summary>Declares fixed fixture resource interfaces and numeric input blocks.</summary>
    private static ShaderStageContract Fixture(string name, string suffix, ShaderStageKind kind)
    {
        var bindings = new GpuBindingContract();
        if (name == "temporal-debug")
        {
            bindings.RegisterShaderStorageBlockBinding("Result", 0);
            bindings.RegisterUniformBlockBinding("VgeFrameUBO", 12);
        }
        string? block = name switch { "rasterizer" => "RasterizerInputs", "particle-draw" => "ParticleDrawInputs", "terrain-capture-opaque" or "terrain-capture-topsoil" => "TerrainCaptureInputs", "normal-input" or "foliage-transmission" or "liquid-interface" => "TestInputs", _ => null };
        if (block != null) bindings.RegisterUniformBlockBinding(block, 28);
        if (name == "sampler-handoff") bindings.RegisterSamplerUnit("ordinaryTexture", 5);
        // The transmission include consumes the fixed energy lookup even when only its diffuse
        // result is asserted. Whole-program preparation must declare that active sampler too.
        if (name == "foliage-transmission") bindings.RegisterSamplerUnit("vge_microfacetEnergy", 32);
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
            _ when name.StartsWith("relief-", StringComparison.Ordinal) => "ReliefInputs",
            _ => "DisplacementInputs"
        };
        bindings.RegisterUniformBlockBinding(block, 28);
        if (name is "displacement-metric" or "displacement-height" or "eye-relative" || name.StartsWith("relief-", StringComparison.Ordinal))
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
