using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>Checks pipeline-description snapshots, formatting, and indexed blend ownership.</summary>
public class GlPipelineDescDebugTests
{
    #region Public API
    #region Construction and formatting
    /// <summary>The constructor retains the diagnostic name for later formatting.</summary>
    [Fact]
    public void Constructor_SetsName()
    {
        // Arrange
        // Act
        var desc = new GlPipelineDesc(
            defaultMask: default,
            nonDefaultMask: default,
            name: "TestName",
            validate: false);

        // Assert
        Assert.Equal("TestName", desc.Name);
    }

    /// <summary>Formatted output includes masks and the resolved values selected by each mask.</summary>
    [Fact]
    public void Format_IncludesName_Masks_AndResolvedKnobs()
    {
        // Arrange
        GlPipelineStateMask defaultMask = default(GlPipelineStateMask)
            .With(GlPipelineStateId.DepthTestEnable);

        GlPipelineStateMask nonDefaultMask = default(GlPipelineStateMask)
            .With(GlPipelineStateId.DepthFunc)
            .With(GlPipelineStateId.DepthWriteMask)
            .With(GlPipelineStateId.LineWidth);

        var desc = new GlPipelineDesc(
            defaultMask: defaultMask,
            nonDefaultMask: nonDefaultMask,
            depthFunc: DepthFunction.Lequal,
            depthWriteMask: false,
            lineWidth: 2f,
            name: "OverlayLines",
            validate: false);

        // Act
        string text = GlPipelineDescDebug.Format(desc);

        // Assert
        Assert.Contains("name=OverlayLines", text);
        Assert.Contains("default=", text);
        Assert.Contains("nonDefault=", text);
        Assert.Contains(nameof(GlPipelineStateId.DepthTestEnable), text);
        Assert.Contains(nameof(GlPipelineStateId.DepthFunc), text);
        Assert.Contains("DepthTest=off", text);
        Assert.Contains("DepthFunc=Lequal", text);
        Assert.Contains("DepthWriteMask=False", text);
        Assert.Contains("LineWidth=2", text);
    }

    /// <summary>Indexed blend output distinguishes default and nondefault attachments in normalized order.</summary>
    [Fact]
    public void Format_IndexedBlend_DefaultAndNonDefault_RenderIntents()
    {
        // Arrange
        GlPipelineStateMask defaultMask = default(GlPipelineStateMask)
            .With(GlPipelineStateId.BlendEnableIndexed);

        GlPipelineStateMask nonDefaultMask = default(GlPipelineStateMask)
            .With(GlPipelineStateId.BlendFuncIndexed);

        byte[] attachments = [5, 4];
        GlBlendFuncIndexed[] blendFuncs =
        [
            new GlBlendFuncIndexed(5, GlBlendFunc.Default),
            new GlBlendFuncIndexed(4, new GlBlendFunc(
                BlendingFactorSrc.SrcAlpha,
                BlendingFactorDest.OneMinusSrcAlpha,
                BlendingFactorSrc.One,
                BlendingFactorDest.OneMinusSrcAlpha)),
        ];

        var desc = new GlPipelineDesc(
            defaultMask: defaultMask,
            nonDefaultMask: nonDefaultMask,
            blendEnableIndexedAttachments: attachments,
            blendFuncIndexed: blendFuncs,
            name: "MrtBlend",
            validate: false);

        // Act
        string text = GlPipelineDescDebug.Format(desc);

        // Assert
        Assert.Contains("BlendIndexed[4,5]=off", text);
        Assert.Contains("BlendFuncIndexed={", text);
        Assert.Contains("4:", text);
        Assert.Contains("5:", text);
    }
    #endregion

    #region Defensive copies
    /// <summary>The constructor sorts and copies indexed attachment input rather than retaining its array.</summary>
    [Fact]
    public void Constructor_ClonesAndSortsAttachments_DoesNotAliasInput()
    {
        // Arrange
        byte[] input = [5, 4];

        GlPipelineStateMask defaultMask = GlPipelineStateMask.From(GlPipelineStateId.BlendEnableIndexed);
        var desc = new GlPipelineDesc(
            defaultMask: defaultMask,
            nonDefaultMask: default,
            blendEnableIndexedAttachments: input,
            validate: false);

        // Act: Mutating the caller's array must not change the constructed description.
        input[0] = 99;

        // Assert
        Assert.NotNull(desc.BlendEnableIndexedAttachments);
        Assert.False(ReferenceEquals(input, desc.BlendEnableIndexedAttachments));
        Assert.Equal<byte>([4, 5], desc.BlendEnableIndexedAttachments!);
    }

    /// <summary>The indexed blend-function input is copied and sorted by attachment index.</summary>
    [Fact]
    public void Constructor_ClonesAndSortsBlendFuncIndexed_DoesNotAliasInput()
    {
        // Arrange
        GlBlendFuncIndexed[] input =
        [
            new GlBlendFuncIndexed(5, GlBlendFunc.Default),
            new GlBlendFuncIndexed(4, GlBlendFunc.Default),
        ];

        GlPipelineStateMask nonDefaultMask = GlPipelineStateMask.From(GlPipelineStateId.BlendFuncIndexed);
        var desc = new GlPipelineDesc(
            defaultMask: default,
            nonDefaultMask: nonDefaultMask,
            blendFuncIndexed: input,
            validate: false);

        // Act: Replace an input item after construction to verify the description owns its snapshot.
        input[0] = new GlBlendFuncIndexed(123, GlBlendFunc.Default);

        // Assert
        Assert.NotNull(desc.BlendFuncIndexed);
        Assert.False(ReferenceEquals(input, desc.BlendFuncIndexed));
        Assert.Equal((byte)4, desc.BlendFuncIndexed![0].AttachmentIndex);
        Assert.Equal((byte)5, desc.BlendFuncIndexed![1].AttachmentIndex);
    }
    #endregion
    #endregion
}

