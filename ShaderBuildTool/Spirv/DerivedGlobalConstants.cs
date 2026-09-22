using TinyTokenizer.Ast;
using VanillaGraphicsExpanded;

namespace ShaderBuildTool.Spirv;

/// <summary>Relaxes global initialized constants whose expressions require executable GLSL evaluation.</summary>
internal static class DerivedGlobalConstants
{
    #region Source transformation
    /// <summary>Removes only the global const keyword; syntax blocks protect local declarations and comments.</summary>
    public static string Apply(string source)
    {
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        var editor = tree.CreateEditor();
        var nodes = tree.Root.Children.ToArray();
        bool hasLayout = false;
        for (int i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i];
            if (node is GlLayoutNode) { hasLayout = true; continue; }
            if (node is SyntaxToken token && token.Kind == GlslSchema.Instance.GetKeywordKind("const") && !hasLayout)
            {
                bool initializer = false, expression = false;
                // Inspect sibling syntax up to the declaration terminator. Parentheses inside
                // comments are trivia, and functions/blocks are never traversed as declarations.
                for (int j = i + 1; j < nodes.Length; j++)
                {
                    var part = nodes[j];
                    if (Query.Symbol(";").Matches(part))
                    {
                        if (initializer && expression)
                        {
                            // Include the next declaration node so the replacement is nonempty.
                            // Edit preserves boundary trivia; interior comments remain verbatim.
                            var range = Query.Between(Query.Exact(token), Query.Exact(nodes[i + 1]), inclusive: true);
                            editor.Edit(range, text => text[token.TextWidth..]);
                        }
                        break;
                    }
                    if (part is GlDirectiveNode or GlFunctionNode) break;
                    if (Query.Operator("=").Matches(part)) initializer = true;
                    if (initializer && ContainsParentheses(part)) expression = true;
                }
            }
            // Layout association ends with the declaration, branch, or complete function/block.
            if (Query.Symbol(";").Matches(node) || node is GlDirectiveNode or GlFunctionNode or GlInterfaceDeclarationNode ||
                node is SyntaxBlock { Kind: NodeKind.BraceBlock }) hasLayout = false;
        }
        editor.Commit();
        return tree.ToText();
    }

    /// <summary>Recognizes nested expression blocks without interpreting their text or trivia.</summary>
    private static bool ContainsParentheses(SyntaxNode node) =>
        node is SyntaxBlock { Kind: NodeKind.ParenBlock } || node.Children.Any(ContainsParentheses);
    #endregion
}
