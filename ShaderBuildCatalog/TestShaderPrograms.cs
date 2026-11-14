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
        var feedbackFragmentBindings = new GpuBindingContract();
        feedbackFragmentBindings.FragmentOutputLocations.Add("result", 0);
        var feedbackFragment = new ShaderStageContract("tests/clip-distance.fsh", "tests/clip-distance.fsh",
            ShaderStageKind.Fragment, feedbackFragmentBindings);
        foreach (string material in new[] { "static", "entity", "terrain" })
        {
            string identity = "tests/shadow-" + material + "-feedback.vsh";
            string productionIdentity = "shadows/" + material + ".vsh";
            var production = GpuShaderContracts.Registry.FindStage(productionIdentity);
            var fixedDefines = new Dictionary<string, ShaderScalar>
            {
                ["VGE_VERTEX_DEFORMATION_ENABLED"] = ShaderScalar.From(1)
            };
            if (material == "static") fixedDefines["VGE_OBJECT_INSTANCED"] = ShaderScalar.From(0);
            if (material == "terrain") fixedDefines["VGE_TERRAIN_PACKED_INPUT"] = ShaderScalar.From(0);
            programs.Add(new("tests/shadow-" + material + "-feedback",
                [new(identity, identity, ShaderStageKind.Vertex, production.Bindings, fixedDefines: fixedDefines), feedbackFragment], 1));
        }
        foreach (string name in new[] { "terrain-capture-opaque", "terrain-capture-topsoil" })
            programs.Add(new("tests/" + name, [Fixture("complete-state", "vsh", ShaderStageKind.Vertex), Fixture(name, "fsh", ShaderStageKind.Fragment)], 1));
        foreach (string name in new[] { "complete-state", "rasterizer", "first-person" })
            programs.Add(new("tests/" + name, [Fixture(name, "vsh", ShaderStageKind.Vertex), Fixture(name, "fsh", ShaderStageKind.Fragment)], 1));
        programs.Add(new("tests/deletion", [Fixture("deletion", "vsh", ShaderStageKind.Vertex), Fixture("complete-state", "fsh", ShaderStageKind.Fragment)], 1));
        var clipping = new GpuBindingContract();
        clipping.FragmentOutputLocations.Add("result", 0);
        programs.Add(new("tests/clip-distance-vertex", [
            new("tests/clip-distance-vertex2.vsh", "tests/clip-distance-vertex2.vsh", ShaderStageKind.Vertex, new()),
            new("tests/clip-distance.fsh", "tests/clip-distance.fsh", ShaderStageKind.Fragment, clipping)], 1));
        programs.Add(new("tests/clip-distance-tessellation", [
            new("tests/clip-distance-tess3.vsh", "tests/clip-distance-tess3.vsh", ShaderStageKind.Vertex, new()),
            new("tests/clip-distance-tess.tesc", "tests/clip-distance-tess.tesc", ShaderStageKind.TessellationControl, new()),
            new("tests/clip-distance-tess.tese", "tests/clip-distance-tess.tese", ShaderStageKind.TessellationEvaluation, new()),
            new("tests/clip-distance.fsh", "tests/clip-distance.fsh", ShaderStageKind.Fragment, clipping)], 1));
        programs.Add(new("tests/clip-distance-tessellation-geometry", [
            new("tests/clip-distance-tess3.vsh", "tests/clip-distance-tess3.vsh", ShaderStageKind.Vertex, new()),
            new("tests/clip-distance-tess.tesc", "tests/clip-distance-tess.tesc", ShaderStageKind.TessellationControl, new()),
            new("tests/clip-distance-tess.tese", "tests/clip-distance-tess.tese", ShaderStageKind.TessellationEvaluation, new()),
            new("tests/clip-distance-tess-geometry3.geom", "tests/clip-distance-tess-geometry3.geom", ShaderStageKind.Geometry, new()),
            new("tests/clip-distance.fsh", "tests/clip-distance.fsh", ShaderStageKind.Fragment, clipping)], 1));
        programs.Add(new("tests/clip-distance-mismatched-tessellation", [
            new("tests/clip-distance-vertex2.vsh", "tests/clip-distance-vertex2.vsh", ShaderStageKind.Vertex, new()),
            new("tests/clip-distance-tess.tesc", "tests/clip-distance-tess.tesc", ShaderStageKind.TessellationControl, new()),
            new("tests/clip-distance-tess.tese", "tests/clip-distance-tess.tese", ShaderStageKind.TessellationEvaluation, new()),
            new("tests/clip-distance.fsh", "tests/clip-distance.fsh", ShaderStageKind.Fragment, clipping)], 1));
        programs.Add(new("tests/clip-distance-geometry", [
            new("tests/clip-distance-geometry4.vsh", "tests/clip-distance-geometry4.vsh", ShaderStageKind.Vertex, new()),
            new("tests/clip-distance-geometry.geom", "tests/clip-distance-geometry.geom", ShaderStageKind.Geometry, new()),
            new("tests/clip-distance.fsh", "tests/clip-distance.fsh", ShaderStageKind.Fragment, clipping)], 1));
        programs.Add(new("tests/clip-distance-geometry-inactive", [
            new("tests/clip-distance-geometry4.vsh", "tests/clip-distance-geometry4.vsh", ShaderStageKind.Vertex, new()),
            new("tests/clip-distance-geometry-inactive.geom", "tests/clip-distance-geometry-inactive.geom", ShaderStageKind.Geometry, new()),
            new("tests/clip-distance.fsh", "tests/clip-distance.fsh", ShaderStageKind.Fragment, clipping)], 1));
        programs.Add(new("tests/clip-distance-inactive", [
            new("tests/clip-distance-inactive.vsh", "tests/clip-distance-inactive.vsh", ShaderStageKind.Vertex, new()),
            new("tests/clip-distance.fsh", "tests/clip-distance.fsh", ShaderStageKind.Fragment, clipping)], 1));
        var atomicWriter = new GpuBindingContract();
        atomicWriter.RegisterShaderStorageBlockBinding("ShadowWriteEntries", 0);
        atomicWriter.RegisterShaderStorageBlockBinding("ShadowWritableAssignments", 1);
        atomicWriter.RegisterUniformBlockBinding("ShadowAtomicWriterInputs", 28);
        atomicWriter.VaryingLocations.Add("atomicDepth", 0);
        atomicWriter.RegisterImageUnit("shadowDepthPool", 0);
        var atomicWriterVertex = new GpuBindingContract();
        atomicWriterVertex.VaryingLocations.Add("atomicDepth", 0);
        programs.Add(new("tests/shadow-atomic-writer", [
            new("tests/shadow-atomic-writer.vsh", "tests/shadow-atomic-writer.vsh", ShaderStageKind.Vertex, atomicWriterVertex),
            new("tests/shadow-atomic-writer.fsh", "tests/shadow-atomic-writer.fsh", ShaderStageKind.Fragment, atomicWriter)], 1));
        var virtualShadowVertex = new GpuBindingContract();
        virtualShadowVertex.RegisterSamplerUnit("shadowViews", 0);
        virtualShadowVertex.VaryingLocations.Add("vge_shadowObject", 0);
        virtualShadowVertex.VaryingLocations.Add("vge_shadowView", 1);
        var virtualShadow = new GpuBindingContract();
        virtualShadow.RegisterSamplerUnit("shadowViews", 0);
        virtualShadow.VaryingLocations.Add("vge_shadowView", 1);
        virtualShadow.RegisterShaderStorageBlockBinding("ShadowWriteEntries", 0);
        virtualShadow.RegisterShaderStorageBlockBinding("ShadowWritableAssignments", 1);
        virtualShadow.RegisterImageUnit("shadowDepthPool", 0);
        programs.Add(new("tests/shadow-virtual", [
            new("tests/shadow-virtual.vsh", "tests/shadow-virtual.vsh", ShaderStageKind.Vertex, virtualShadowVertex),
            new("tests/shadow-virtual.fsh", "tests/shadow-virtual.fsh", ShaderStageKind.Fragment, virtualShadow)], 1));
        programs.Add(new("tests/temporal-debug", [Fixture("temporal-debug", "csh", ShaderStageKind.Compute)], 1));
        var deformationInputs = new GpuBindingContract();
        deformationInputs.RegisterShaderStorageBlockBinding("DeformationResults", 0);
        deformationInputs.RegisterUniformBlockBinding("DeformationInputs", 28);
        programs.Add(new("tests/shared-deformation-numeric",
            [new("tests/shared-deformation-numeric.csh", "tests/shared-deformation-numeric.csh", ShaderStageKind.Compute, deformationInputs)], 1));
        var surfaceWorkCollector = new GpuBindingContract();
        surfaceWorkCollector.RegisterShaderStorageBlockBinding("DiagnosticCounters", 4);
        programs.Add(new("tests/surface-work-collector",
            [new("tests/surface-work-collector.csh", "tests/surface-work-collector.csh", ShaderStageKind.Compute, surfaceWorkCollector)], 1));
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
            bindings.RegisterSamplerUnit("vge_atmosphereAerialRadiance", 11);
            bindings.RegisterSamplerUnit("vge_atmosphereAerialAttenuation", 12);
            bindings.RegisterSamplerUnit("vge_lightShaftOcclusion", 13);
            bindings.RegisterSamplerUnit("vge_microfacetEnergy", 32);
            // The forward helper imports the shared surface interface, so the RHI executable contract
            // must carry the same typed VSM resources as production surface programs.
            var surface = GpuShaderContracts.Create("pbr_direct_lighting");
            bindings.UniformBlocks.Add("SurfaceShadowParameters", surface.UniformBlocks["SurfaceShadowParameters"]);
            foreach (string name in new[] { "SurfaceShadowRequests", "PageEntries", "SurfaceShadowReadiness", "SurfaceShadowDiagnostics" })
                bindings.StorageBlocks.Add(name, surface.StorageBlocks[name]);
            bindings.Samplers.Add("shadowDepthPool", surface.Samplers["shadowDepthPool"]);
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
