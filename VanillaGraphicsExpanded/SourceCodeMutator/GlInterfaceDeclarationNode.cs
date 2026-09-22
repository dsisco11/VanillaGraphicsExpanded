using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded;

/// <summary>Storage namespaces used by named GLSL interface declarations.</summary>
public enum GlInterfaceStorage
{
    Uniform,
    Buffer,
    Input,
    Output
}

/// <summary>Typed interface shared by standalone uniforms, stage values and interface block headers.</summary>
public abstract class GlInterfaceDeclarationNode : SyntaxNode, INamedNode
{
    #region Construction
    /// <summary>Wraps the declaration recognized by the GLSL schema.</summary>
    protected GlInterfaceDeclarationNode(CreationContext context) : base(context) { }
    #endregion

    #region Declaration properties
    /// <summary>The declared resource or interface name.</summary>
    public abstract string Name { get; }
    /// <summary>The GLSL storage namespace containing this declaration.</summary>
    public abstract GlInterfaceStorage Storage { get; }
    /// <summary>Whether this declaration introduces an interface block rather than a standalone value.</summary>
    public virtual bool IsBlock => false;
    /// <summary>Whether this value uses atomic-counter bindings rather than ordinary uniform locations.</summary>
    public virtual bool IsAtomicCounter => false;
    #endregion
}
