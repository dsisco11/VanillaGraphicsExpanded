using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded;

/// <summary>A named stage input or output, with an optional array dimension.</summary>
public sealed class GlStageIoNode : GlInterfaceDeclarationNode
{
    #region Construction
    /// <summary>Wraps a stage declaration recognized by the GLSL schema.</summary>
    internal GlStageIoNode(CreationContext context) : base(context) { }
    #endregion

    #region Declaration properties
    /// <inheritdoc/>
    public override string Name => GetTypedChild<SyntaxToken>(2).Text;
    /// <inheritdoc/>
    public override GlInterfaceStorage Storage => GetTypedChild<SyntaxToken>(0).Kind == GlslSchema.Instance.GetKeywordKind("in")
        ? GlInterfaceStorage.Input : GlInterfaceStorage.Output;
    #endregion
}
