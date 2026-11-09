using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>Checks validation and normalization for masked pipeline-description values.</summary>
public class GlPipelineDescValidationTests
{
    #region Public API
    #region Required values
    /// <summary>A selected nondefault scalar blend function requires a value.</summary>
    [Fact]
    public void ValidateDesc_NonDefaultBlendFunc_RequiresValue()
    {
        // Arrange
        GlPipelineStateMask nonDefault = GlPipelineStateMask.From(GlPipelineStateId.BlendFunc);
        var desc = new GlPipelineDesc(defaultMask: default, nonDefaultMask: nonDefault, validate: false);

        // Act and Assert
        Assert.Throws<ArgumentException>(() => GlPipelineStateValidation.ValidateDesc(desc));
    }

    /// <summary>A supplied scalar blend function satisfies the selected state.</summary>
    [Fact]
    public void ValidateDesc_NonDefaultBlendFunc_WithValue_Passes()
    {
        // Arrange
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

        // Act and Assert
        GlPipelineStateValidation.ValidateDesc(desc);
    }

    /// <summary>Indexed blend-enable state requires attachment entries.</summary>
    [Fact]
    public void ValidateDesc_BlendEnableIndexed_RequiresAttachmentList()
    {
        // Arrange
        GlPipelineStateMask defaultMask = GlPipelineStateMask.From(GlPipelineStateId.BlendEnableIndexed);
        var desc = new GlPipelineDesc(defaultMask: defaultMask, nonDefaultMask: default, validate: false);

        // Act and Assert
        Assert.Throws<ArgumentException>(() => GlPipelineStateValidation.ValidateDesc(desc));
    }

    /// <summary>Duplicate indexed blend-enable entries are rejected.</summary>
    [Fact]
    public void ValidateDesc_BlendEnableIndexed_WithDuplicateIndices_Throws()
    {
        // Arrange
        GlPipelineStateMask defaultMask = GlPipelineStateMask.From(GlPipelineStateId.BlendEnableIndexed);
        var desc = new GlPipelineDesc(
            defaultMask: defaultMask,
            nonDefaultMask: default,
            blendEnableIndexedAttachments: [5, 4, 4],
            validate: false);

        // Act and Assert
        Assert.Throws<ArgumentException>(() => GlPipelineStateValidation.ValidateDesc(desc));
    }

    /// <summary>The constructor sorts indexed blend-enable attachments by index.</summary>
    [Fact]
    public void Constructor_NormalizesBlendEnableIndexedAttachmentOrder()
    {
        // Arrange
        GlPipelineStateMask defaultMask = GlPipelineStateMask.From(GlPipelineStateId.BlendEnableIndexed);
        // Act
        var desc = new GlPipelineDesc(
            defaultMask: defaultMask,
            nonDefaultMask: default,
            blendEnableIndexedAttachments: [5, 4],
            validate: false);

        // Assert: Constructor normalization orders the copied attachments.
        Assert.NotNull(desc.BlendEnableIndexedAttachments);
        Assert.Equal<byte>([4, 5], desc.BlendEnableIndexedAttachments!);
    }

    /// <summary>Indexed blend-function state requires value entries.</summary>
    [Fact]
    public void ValidateDesc_BlendFuncIndexed_RequiresValues()
    {
        // Arrange
        GlPipelineStateMask defaultMask = GlPipelineStateMask.From(GlPipelineStateId.BlendFuncIndexed);
        var desc = new GlPipelineDesc(defaultMask: defaultMask, nonDefaultMask: default, validate: false);

        // Act and Assert
        Assert.Throws<ArgumentException>(() => GlPipelineStateValidation.ValidateDesc(desc));
    }

    /// <summary>The constructor sorts indexed blend functions by attachment index.</summary>
    [Fact]
    public void Constructor_NormalizesBlendFuncIndexedAttachmentOrder()
    {
        // Arrange
        GlPipelineStateMask nonDefault = GlPipelineStateMask.From(GlPipelineStateId.BlendFuncIndexed);
        // Act
        var desc = new GlPipelineDesc(
            defaultMask: default,
            nonDefaultMask: nonDefault,
            blendFuncIndexed:
            [
                new GlBlendFuncIndexed(5, GlBlendFunc.Default),
                new GlBlendFuncIndexed(4, GlBlendFunc.Default),
            ],
            validate: false);

        // Assert: Constructor normalization orders the copied blend-function values.
        Assert.NotNull(desc.BlendFuncIndexed);
        Assert.Equal((byte)4, desc.BlendFuncIndexed![0].AttachmentIndex);
        Assert.Equal((byte)5, desc.BlendFuncIndexed![1].AttachmentIndex);
    }
    #endregion
    #endregion
}

