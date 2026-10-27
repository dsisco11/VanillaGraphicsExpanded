using VanillaGraphicsExpanded.Rendering.Pipeline;
using System;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies dormant-to-active native transitions across target aspects and independent selector families.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class RelevantGraphicsMatrixTests(HeadlessGLFixture fixture) : IDisposable
{
    private readonly GraphicsStateShaders stateShaders = new();
    /// <summary>Retires test-owned executable generations after state assertions complete.</summary>
    public void Dispose() => stateShaders.Dispose();

    #region Public API
    /// <summary>Integer outputs bypass blending while float outputs and color masks establish their own routed intent.</summary>
    [Fact]
    public void MixedIntegerAndFloatingRoutesPreserveIntegerBlendPayloads()
    {
        fixture.MakeCurrent(); var cache = StateCache.Current; OpenGLProgramSelection.Select(cache, 0);
        var baseline = Description();
        var description = new RHIGraphicsPipelineDescription(baseline.Shader, baseline.VertexLayout,
            new([new(TextureFormat.Rgba8ui), new(null, true), new(TextureFormat.Rgba8)]),
            DynamicPipelineState.Viewport, blending: [new() { WriteRed = false }, new(), new()]);
        foreach (int slot in new[] { 0, 1, 2 })
        {
            GL.Enable(IndexedEnableCap.Blend, slot); GL.BlendEquationSeparate(slot, BlendEquationMode.Max, BlendEquationMode.Min);
            GL.BlendFuncSeparate(slot, BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
                BlendingFactorSrc.Zero, BlendingFactorDest.One); GL.ColorMask(slot, true, true, true, true);
        }
        cache.InvalidateAll();
        try
        {
            Assert.True(((Func<bool>)(() => {
                if (!true) return false;
                var coverage = PipelineStateCoverage.Empty;
                foreach (RHIGraphicsPipelineDescription declaration in new RHIGraphicsPipelineDescription[] {description})
                    coverage = coverage.Union(PipelineStateCoverage.From(VanillaGraphicsExpanded.Rendering.Pipeline.GraphicsStateRelevance.Derive(declaration, GpuSupport.Graphics)));
                if (!StateCache.Current.TryBeginEngineBoundary(new EngineBoundaryDeclaration("IntegerAndSparseRoutes", coverage), out var restored,
                    new EngineBoundaryResources(geometry: true, programSelection: true))) return false;
                using (restored) {
                    Action<EngineBoundaryScope> operation = _ =>
            {
                cache.ApplyGraphicsState(description, Dynamics());
                Assert.True(GL.IsEnabled(IndexedEnableCap.Blend, 0)); Assert.True(GL.IsEnabled(IndexedEnableCap.Blend, 1));
                Assert.False(GL.IsEnabled(IndexedEnableCap.Blend, 2));
                bool[] mask = new bool[4]; GL.GetBoolean(GetIndexedPName.ColorWritemask, 0, mask);
                Assert.Equal(new[] { false, true, true, true }, mask);
                foreach (int slot in new[] { 0, 1, 2 })
                {
                    GL.GetInteger((GetIndexedPName)GetPName.BlendEquationRgb, slot, out int equation);
                    GL.GetInteger((GetIndexedPName)GetPName.BlendSrcRgb, slot, out int source);
                    Assert.Equal((int)BlendEquationMode.Max, equation); Assert.Equal((int)BlendingFactorSrc.SrcAlpha, source);
                }
            };
                    restored!.Run(() => operation(restored));
                }
                return true;
            }))());
            foreach (int slot in new[] { 0, 1, 2 }) Assert.True(GL.IsEnabled(IndexedEnableCap.Blend, slot));
            bool[] restored = new bool[4]; GL.GetBoolean(GetIndexedPName.ColorWritemask, 0, restored); Assert.All(restored, Assert.True);
        }
        finally { foreach (int slot in new[] { 0, 1, 2 }) GL.Disable(IndexedEnableCap.Blend, slot); cache.InvalidateAll(); }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Missing aspects leave even enable bits untouched; present disabled tests preserve payloads until activation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DepthStencilAspectsAndTestsControlPayloadApplication(bool fail)
    {
        fixture.MakeCurrent(); var cache = StateCache.Current; OpenGLProgramSelection.Select(cache, 0);
        var intent = new DepthStencilDesc { DepthTest = true, DepthWrite = true, DepthComparison = ComparisonFunction.Greater,
            StencilTest = true, Front = new() { Comparison = ComparisonFunction.Equal, ReadMask = 0x71, WriteMask = 0x37,
                Pass = StencilOperation.Replace }, Back = new() { Comparison = ComparisonFunction.NotEqual, ReadMask = 0x62,
                WriteMask = 0x46, Pass = StencilOperation.IncrementClamp } };
        var absent = Description(depth: intent with { DepthTest = false, DepthWrite = false, StencilTest = false });
        var disabled = Description(depth: intent with { DepthTest = false, StencilTest = false }, aspects: TextureFormat.Depth24Stencil8);
        var active = Description(depth: intent, aspects: TextureFormat.Depth24Stencil8);
        GL.Enable(EnableCap.DepthTest); GL.Enable(EnableCap.StencilTest); GL.DepthFunc(DepthFunction.Lequal); GL.DepthMask(false);
        GL.StencilFuncSeparate(StencilFace.Front, StencilFunction.Greater, 3, 0x53);
        GL.StencilFuncSeparate(StencilFace.Back, StencilFunction.Less, 4, 0x64);
        GL.StencilMaskSeparate(StencilFace.Front, 0x25); GL.StencilMaskSeparate(StencilFace.Back, 0x36);
        GL.StencilOpSeparate(StencilFace.Front, StencilOp.Keep, StencilOp.Invert, StencilOp.Zero);
        GL.StencilOpSeparate(StencilFace.Back, StencilOp.Incr, StencilOp.Decr, StencilOp.Keep);
        cache.InvalidateAll(); var inherited = ReadDepthStencil();
        var expected = new InvalidOperationException("matrix after dormant final operation");
        try
        {
            bool Run() => ((Func<bool>)(() => {
                if (!true) return false;
                var coverage = PipelineStateCoverage.Empty;
                foreach (RHIGraphicsPipelineDescription declaration in new RHIGraphicsPipelineDescription[] {absent, disabled, active})
                    coverage = coverage.Union(PipelineStateCoverage.From(VanillaGraphicsExpanded.Rendering.Pipeline.GraphicsStateRelevance.Derive(declaration, GpuSupport.Graphics)));
                if (!StateCache.Current.TryBeginEngineBoundary(new EngineBoundaryDeclaration("RelevantAspects", coverage), out var restored,
                    new EngineBoundaryResources(geometry: true, programSelection: true))) return false;
                using (restored) {
                    Action<EngineBoundaryScope> operation = _ =>
            {
                cache.ApplyGraphicsState(absent, Dynamics()); Assert.Equal(inherited, ReadDepthStencil());
                cache.ApplyGraphicsState(disabled, Dynamics());
                Assert.False(GL.IsEnabled(EnableCap.DepthTest)); Assert.False(GL.IsEnabled(EnableCap.StencilTest));
                Assert.Equal(inherited.Skip(2), ReadDepthStencil().Skip(2));
                cache.ApplyGraphicsState(active, Dynamics());
                Assert.True(GL.IsEnabled(EnableCap.DepthTest)); Assert.True(GL.IsEnabled(EnableCap.StencilTest));
                Assert.Equal((int)DepthFunction.Greater, GL.GetInteger(GetPName.DepthFunc)); Assert.True(GL.GetBoolean(GetPName.DepthWritemask));
                Assert.Equal((int)StencilFunction.Equal, GL.GetInteger(GetPName.StencilFunc));
                Assert.Equal((int)StencilFunction.Notequal, GL.GetInteger(GetPName.StencilBackFunc));
                Assert.Equal(5, GL.GetInteger(GetPName.StencilRef)); Assert.Equal(6, GL.GetInteger(GetPName.StencilBackRef));
                Assert.Equal(0x37, GL.GetInteger(GetPName.StencilWritemask)); Assert.Equal(0x46, GL.GetInteger(GetPName.StencilBackWritemask));
                var activated = ReadDepthStencil();
                cache.ApplyGraphicsState(absent, Dynamics()); Assert.Equal(activated, ReadDepthStencil());
                if (fail) throw expected;
            };
                    restored!.Run(() => operation(restored));
                }
                return true;
            }))();
            if (fail) Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => Run())); else Assert.True(Run());
            Assert.Equal(inherited, ReadDepthStencil());
        }
        finally { GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.StencilTest); cache.InvalidateAll(); }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Sampling and assembly enable transitions establish retained payloads and preserve every dormant operand on exit.</summary>
    [Fact]
    public void SamplingAndAssemblyPayloadsActivateThenRestore()
    {
        fixture.MakeCurrent(); var cache = StateCache.Current; OpenGLProgramSelection.Select(cache, 0);
        var dormant = Description(sampling: new() { Coverage = .75f, CoverageInvert = false, Masks = new([0x0f0ff0f0u]),
                MinimumSampleShading = .625f }, assembly: new() { RestartIndex = 23, PatchVertices = 4 });
        var enabled = Description(sampling: dormant.Sampling with { CoverageEnabled = true, MaskEnabled = true, SampleShading = true },
            assembly: dormant.Assembly with { Restart = true });
        var fixedRestart = Description(assembly: new() { FixedIndexRestart = true, RestartIndex = 29 });
        var patches = Description(assembly: new() { Topology = PrimitiveTopology.Patches, PatchVertices = 4 });
        GL.SampleCoverage(.375f, true); GL.MinSampleShading(.25f); GL.PrimitiveRestartIndex(17); GL.PatchParameter(PatchParameterInt.PatchVertices, 3);
        GL.Disable(EnableCap.SampleCoverage); GL.Disable(EnableCap.SampleMask); GL.Disable(EnableCap.SampleShading);
        GL.Disable(EnableCap.PrimitiveRestart); GL.Disable(EnableCap.PrimitiveRestartFixedIndex);
        for (int word = 0; word < GpuSupport.Graphics.MaxSampleMaskWords; word++) GL.SampleMask((uint)word, 0x55aa55aau);
        cache.InvalidateAll(); var inherited = ReadSamplingAssembly();
        try
        {
            Assert.True(((Func<bool>)(() => {
                if (!true) return false;
                var coverage = PipelineStateCoverage.Empty;
                foreach (RHIGraphicsPipelineDescription declaration in new RHIGraphicsPipelineDescription[] {dormant, enabled, fixedRestart, patches})
                    coverage = coverage.Union(PipelineStateCoverage.From(VanillaGraphicsExpanded.Rendering.Pipeline.GraphicsStateRelevance.Derive(declaration, GpuSupport.Graphics)));
                if (!StateCache.Current.TryBeginEngineBoundary(new EngineBoundaryDeclaration("RelevantSamplingAssembly", coverage), out var restored,
                    new EngineBoundaryResources(geometry: true, programSelection: true))) return false;
                using (restored) {
                    Action<EngineBoundaryScope> operation = _ =>
            {
                cache.ApplyGraphicsState(dormant, Dynamics()); Assert.Equal(inherited, ReadSamplingAssembly());
                cache.ApplyGraphicsState(enabled, Dynamics());
                Assert.True(GL.IsEnabled(EnableCap.SampleCoverage)); Assert.Equal(.75f, GL.GetFloat(GetPName.SampleCoverageValue));
                Assert.False(GL.GetBoolean(GetPName.SampleCoverageInvert)); Assert.True(GL.IsEnabled(EnableCap.SampleMask));
                for (int word = 0; word < GpuSupport.Graphics.MaxSampleMaskWords; word++)
                {
                    GL.GetInteger((GetIndexedPName)All.SampleMaskValue, word, out int mask);
                    Assert.Equal(word == 0 ? 0x0f0ff0f0u : uint.MaxValue, unchecked((uint)mask));
                }
                Assert.True(GL.IsEnabled(EnableCap.SampleShading)); Assert.Equal(.625f, GL.GetFloat(GetPName.MinSampleShadingValue));
                Assert.Equal(23, GL.GetInteger(GetPName.PrimitiveRestartIndex)); Assert.Equal(3, GL.GetInteger(GetPName.PatchVertices));
                cache.ApplyGraphicsState(fixedRestart, Dynamics());
                Assert.True(GL.IsEnabled(EnableCap.PrimitiveRestartFixedIndex)); Assert.Equal(23, GL.GetInteger(GetPName.PrimitiveRestartIndex));
                cache.ApplyGraphicsState(patches, Dynamics()); Assert.Equal(4, GL.GetInteger(GetPName.PatchVertices));
                cache.ApplyGraphicsState(dormant, Dynamics());
                Assert.Equal(4, GL.GetInteger(GetPName.PatchVertices)); Assert.Equal(23, GL.GetInteger(GetPName.PrimitiveRestartIndex));
                Assert.Equal(.75f, GL.GetFloat(GetPName.SampleCoverageValue)); Assert.Equal(.625f, GL.GetFloat(GetPName.MinSampleShadingValue));
            };
                    restored!.Run(() => operation(restored));
                }
                return true;
            }))());
            Assert.Equal(inherited, ReadSamplingAssembly());
        }
        finally { GL.Disable(EnableCap.SampleCoverage); GL.Disable(EnableCap.SampleMask); GL.Disable(EnableCap.SampleShading);
            GL.Disable(EnableCap.PrimitiveRestart); GL.Disable(EnableCap.PrimitiveRestartFixedIndex); cache.InvalidateAll(); }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Uses a complete declaration with explicit stencil dynamics, including dormant declarations.</summary>
    private RHIGraphicsPipelineDescription Description(DepthStencilDesc? depth = null, TextureFormat? aspects = null,
        SamplingDesc? sampling = null, PrimitiveAssemblyDesc? assembly = null)
    {
        var stages = new List<ShaderStageContract> { new("matrix.vsh", "matrix.vsh", ShaderStageKind.Vertex, new()),
            new("matrix.fsh", "matrix.fsh", ShaderStageKind.Fragment, new()) };
        if (assembly?.Topology == PrimitiveTopology.Patches)
        {
            stages.Add(new("matrix.tcs", "matrix.tcs", ShaderStageKind.TessellationControl, new()));
            stages.Add(new("matrix.tes", "matrix.tes", ShaderStageKind.TessellationEvaluation, new()));
        }
        var contract = new GpuShaderContract("matrix", stages, 1);
        return new(stateShaders.Get(contract.Stages.Select(stage => stage.Kind)), new([]), new([new(TextureFormat.Rgba8)], aspects),
            DynamicPipelineState.Viewport | DynamicPipelineState.StencilReference, depthStencil: depth, sampling: sampling, assembly: assembly);
    }

    /// <summary>Supplies valid retained intent even to draws that do not use stencil references.</summary>
    private static RHIDynamicState Dynamics() => new() { Viewport = new() { Width = 8, Height = 8 }, StencilReference = (5, 6) };

    /// <summary>Reads both stencil faces and all depth values independently from the shared cache.</summary>
    private static int[] ReadDepthStencil() => [GL.IsEnabled(EnableCap.DepthTest) ? 1 : 0, GL.IsEnabled(EnableCap.StencilTest) ? 1 : 0,
        GL.GetInteger(GetPName.DepthFunc), GL.GetBoolean(GetPName.DepthWritemask) ? 1 : 0,
        GL.GetInteger(GetPName.StencilFunc), GL.GetInteger(GetPName.StencilRef), GL.GetInteger(GetPName.StencilValueMask),
        GL.GetInteger(GetPName.StencilWritemask), GL.GetInteger(GetPName.StencilFail), GL.GetInteger(GetPName.StencilPassDepthFail), GL.GetInteger(GetPName.StencilPassDepthPass),
        GL.GetInteger(GetPName.StencilBackFunc), GL.GetInteger(GetPName.StencilBackRef), GL.GetInteger(GetPName.StencilBackValueMask),
        GL.GetInteger(GetPName.StencilBackWritemask), GL.GetInteger(GetPName.StencilBackFail), GL.GetInteger(GetPName.StencilBackPassDepthFail), GL.GetInteger(GetPName.StencilBackPassDepthPass)];

    /// <summary>Captures enable bits, dependent payloads, every mask word and assembly operands from native state.</summary>
    private static float[] ReadSamplingAssembly()
    {
        var values = new List<float> { GL.IsEnabled(EnableCap.SampleCoverage) ? 1 : 0, GL.GetFloat(GetPName.SampleCoverageValue),
            GL.GetBoolean(GetPName.SampleCoverageInvert) ? 1 : 0, GL.IsEnabled(EnableCap.SampleMask) ? 1 : 0,
            GL.IsEnabled(EnableCap.SampleShading) ? 1 : 0, GL.GetFloat(GetPName.MinSampleShadingValue),
            GL.IsEnabled(EnableCap.PrimitiveRestart) ? 1 : 0, GL.IsEnabled(EnableCap.PrimitiveRestartFixedIndex) ? 1 : 0,
            GL.GetInteger(GetPName.PrimitiveRestartIndex), GL.GetInteger(GetPName.PatchVertices) };
        for (int word = 0; word < GpuSupport.Graphics.MaxSampleMaskWords; word++)
        {
            GL.GetInteger((GetIndexedPName)All.SampleMaskValue, word, out int mask);
            // Bit reinterpretation avoids rounding any bit pattern in a float comparison container.
            values.Add(BitConverter.Int32BitsToSingle(mask));
        }
        return values.ToArray();
    }
    #endregion
}
