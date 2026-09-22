using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded;

/// <summary>A uniform or storage block header, including headers separated from their body by preprocessing directives.</summary>
public sealed class GlInterfaceBlockHeaderNode : GlInterfaceDeclarationNode
{
    #region Construction
    /// <summary>Wraps a block header recognized by the GLSL schema.</summary>
    internal GlInterfaceBlockHeaderNode(CreationContext context) : base(context) { }
    #endregion

    #region Declaration properties
    /// <inheritdoc/>
    public override string Name => GetTypedChild<SyntaxToken>(1).Text;
    /// <inheritdoc/>
    public override GlInterfaceStorage Storage => GetTypedChild<SyntaxToken>(0).Kind == GlslSchema.Instance.GetKeywordKind("uniform")
        ? GlInterfaceStorage.Uniform : GlInterfaceStorage.Buffer;
    /// <inheritdoc/>
    public override bool IsBlock => true;
    #endregion
}
