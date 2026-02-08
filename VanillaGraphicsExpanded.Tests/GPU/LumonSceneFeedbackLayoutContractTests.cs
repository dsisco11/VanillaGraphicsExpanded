using System;
using System.IO;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumonSceneFeedbackLayoutContractTests : RenderTestBase
{
    public LumonSceneFeedbackLayoutContractTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void FeedbackMarkPages_GlslPath_ExposesExpectedBindings()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();

        var layout = new GpuProgramLayout();
        layout.RegisterUniformBlockBinding("VgeLumOnSceneFeedbackMarkParamsUBO", GpuBindingRegistry.Ubo.Object, required: true);
        layout.RegisterSamplerUnit("vge_patchIdGBuffer", unit: 0, required: true);
        layout.RegisterSamplerUnit("vge_chunkSlotGenerationTex", unit: 1, required: false);
        layout.RegisterImageUnit("vge_pageUsageStamp", unit: 0, required: true);

        using var program = ComputeProgram.Create(
            helper,
            computeShaderFile: "lumonscene_feedback_mark_pages.csh",
            debugName: "Tests.LayoutContract.FeedbackMarkPages.Glsl",
            preferSpirv: false,
            layout: layout,
            layoutWarn: message => Assert.Fail(message));

        Assert.NotEqual(0, program.ProgramId);
    }

    [Fact]
    public void FeedbackCompactPages_GlslPath_ExposesExpectedBindings()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();

        var layout = new GpuProgramLayout();
        layout.RegisterUniformBlockBinding("VgeLumOnSceneFeedbackCompactParamsUBO", GpuBindingRegistry.Ubo.Object, required: true);
        layout.RegisterSamplerUnit("vge_pageUsageStamp", unit: 0, required: true);
        layout.RegisterSamplerUnit("vge_pageTableMip0", unit: 1, required: true);
        layout.RegisterShaderStorageBlockBinding("VgePageRequests", bindingIndex: 0, required: true);

        using var program = ComputeProgram.Create(
            helper,
            computeShaderFile: "lumonscene_feedback_compact_pages.csh",
            debugName: "Tests.LayoutContract.FeedbackCompactPages.Glsl",
            preferSpirv: false,
            layout: layout,
            layoutWarn: message => Assert.Fail(message));

        Assert.NotEqual(0, program.ProgramId);
    }

    private static ShaderTestHelper CreateShaderHelperOrSkip()
    {
        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        if (!Directory.Exists(shaderPath) || !Directory.Exists(includePath))
        {
            Assert.Skip("Shader assets not available - test output content may be missing");
        }

        return new ShaderTestHelper(shaderPath, includePath);
    }
}
