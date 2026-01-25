using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests;

public class GlPipelineDescPhase2Tests
{
    [Fact]
    public void ValidateDesc_NonDefaultBlendFunc_RequiresValue()
    {
        GlPipelineStateMask nonDefault = GlPipelineStateMask.From(GlPipelineStateId.BlendFunc);
        var desc = new GlPipelineDesc(defaultMask: default, nonDefaultMask: nonDefault, validate: false);

        Assert.Throws<ArgumentException>(() => GlPipelineStateValidation.ValidateDesc(desc));
    }

    [Fact]
    public void ValidateDesc_NonDefaultBlendFunc_WithValue_Passes()
    {
        GlPipelineStateMask nonDefault = GlPipelineStateMask.From(GlPipelineStateId.BlendFunc);
        var desc = new GlPipelineDesc(
            defaultMask: default,
            nonDefaultMask: nonDefault,
            blendFunc: new GlBlendFunc(
                BlendingFactorSrc.SrcAlpha,
                BlendingFactorDest.OneMinusSrcAlpha,
                BlendingFactorSrc.One,
                BlendingFactorDest.OneMinusSrcAlpha),
            validate: false);

        GlPipelineStateValidation.ValidateDesc(desc);
    }

    [Fact]
    public void ValidateDesc_BlendEnableIndexed_RequiresAttachmentList()
    {
        GlPipelineStateMask defaultMask = GlPipelineStateMask.From(GlPipelineStateId.BlendEnableIndexed);
        var desc = new GlPipelineDesc(defaultMask: defaultMask, nonDefaultMask: default, validate: false);

        Assert.Throws<ArgumentException>(() => GlPipelineStateValidation.ValidateDesc(desc));
    }

    [Fact]
    public void ValidateDesc_BlendEnableIndexed_WithDuplicateIndices_Throws()
    {
        GlPipelineStateMask defaultMask = GlPipelineStateMask.From(GlPipelineStateId.BlendEnableIndexed);
        var desc = new GlPipelineDesc(
            defaultMask: defaultMask,
            nonDefaultMask: default,
            blendEnableIndexedAttachments: [5, 4, 4],
            validate: false);

        Assert.Throws<ArgumentException>(() => GlPipelineStateValidation.ValidateDesc(desc));
    }

    [Fact]
    public void ValidateDesc_BlendEnableIndexed_NormalizesSortOrder()
    {
        GlPipelineStateMask defaultMask = GlPipelineStateMask.From(GlPipelineStateId.BlendEnableIndexed);
        var desc = new GlPipelineDesc(
            defaultMask: defaultMask,
            nonDefaultMask: default,
            blendEnableIndexedAttachments: [5, 4],
            validate: false);

        Assert.NotNull(desc.BlendEnableIndexedAttachments);
        Assert.Equal<byte>([4, 5], desc.BlendEnableIndexedAttachments!);
    }

    [Fact]
    public void ValidateDesc_BlendFuncIndexed_RequiresValues()
    {
        GlPipelineStateMask defaultMask = GlPipelineStateMask.From(GlPipelineStateId.BlendFuncIndexed);
        var desc = new GlPipelineDesc(defaultMask: defaultMask, nonDefaultMask: default, validate: false);

        Assert.Throws<ArgumentException>(() => GlPipelineStateValidation.ValidateDesc(desc));
    }

    [Fact]
    public void ValidateDesc_BlendFuncIndexed_SortsByAttachmentIndex()
    {
        GlPipelineStateMask nonDefault = GlPipelineStateMask.From(GlPipelineStateId.BlendFuncIndexed);
        var desc = new GlPipelineDesc(
            defaultMask: default,
            nonDefaultMask: nonDefault,
            blendFuncIndexed:
            [
                new GlBlendFuncIndexed(5, GlBlendFunc.Default),
                new GlBlendFuncIndexed(4, GlBlendFunc.Default),
            ],
            validate: false);

        Assert.NotNull(desc.BlendFuncIndexed);
        Assert.Equal((byte)4, desc.BlendFuncIndexed![0].AttachmentIndex);
        Assert.Equal((byte)5, desc.BlendFuncIndexed![1].AttachmentIndex);
    }
}
