using TinyTokenizer.Ast;
using VanillaGraphicsExpanded;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderBuildTool.Spirv;

/// <summary>Applies GPU contract layouts to typed GLSL declarations before the shader compiler runs.</summary>
internal static class ShaderSourceLayout
{
    /// <summary>Interface roles used when assigning stage input and output locations.</summary>
    private enum StageRole { Vertex, Fragment, Compute, Intermediate }

    #region Source layout emission
    /// <summary>Edits top-level interface nodes; function parameters, bodies and block members remain untouched.</summary>
    public static string Apply(string source, string stage, GpuBindingContract contract)
    {
        var role = stage switch
        {
            "vsh" => StageRole.Vertex,
            "fsh" => StageRole.Fragment,
            "csh" => StageRole.Compute,
            "gsh" or "tcsh" or "tesh" => StageRole.Intermediate,
            _ => throw new ArgumentException("Unsupported shader stage: " + stage, nameof(stage))
        };
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        var editor = tree.CreateEditor();
        var layouts = new List<GlLayoutNode>();
        SyntaxNode? firstQualifier = null;
        // Qualifiers precede their declaration as sibling syntax nodes. Directives and other
        // constructs terminate that association, including conditional interface-block headers.
        foreach (var node in tree.Root.Children)
        {
            switch (node)
            {
                case GlLayoutNode layout:
                    firstQualifier ??= node;
                    layouts.Add(layout);
                    continue;
                case SyntaxToken qualifier when GlslSchema.StorageQualifierKinds.Contains(qualifier.Kind):
                    firstQualifier ??= node;
                    continue;
                case GlInterfaceDeclarationNode declaration:
                    ApplyDeclaration(declaration, firstQualifier ?? node, layouts, role, contract, editor);
                    break;
            }
            layouts.Clear();
            firstQualifier = null;
        }
        editor.Commit();
        return tree.ToText();
    }

    /// <summary>Resolves contract-owned qualifiers from the declaration's typed storage namespace.</summary>
    private static void ApplyDeclaration(GlInterfaceDeclarationNode declaration, SyntaxNode insertionPoint,
        List<GlLayoutNode> layouts, StageRole stage, GpuBindingContract contract, SyntaxEditor editor)
    {
        string name = declaration.Name;
        var values = new Dictionary<GlLayoutQualifierKind, int>();
        if (declaration.Storage is GlInterfaceStorage.Uniform or GlInterfaceStorage.Buffer)
        {
            if (declaration.IsBlock)
            {
                var bindings = declaration.Storage == GlInterfaceStorage.Uniform ? contract.UniformBlocks : contract.StorageBlocks;
                if (bindings.TryGetValue(name, out var binding)) values.Add(GlLayoutQualifierKind.Binding, binding.Slot);
            }
            else if (!declaration.IsAtomicCounter)
            {
                if (contract.UniformLocations.TryGetValue(name, out int location)) values.Add(GlLayoutQualifierKind.Location, location);
                if (contract.Samplers.TryGetValue(name, out var sampler)) values.Add(GlLayoutQualifierKind.Binding, sampler.Slot);
                else if (contract.Images.TryGetValue(name, out var image)) values.Add(GlLayoutQualifierKind.Binding, image.Slot);
            }
        }
        else if (stage != StageRole.Compute)
        {
            bool attachment = declaration.Storage == GlInterfaceStorage.Output && stage == StageRole.Fragment;
            var locations = attachment ? contract.FragmentOutputLocations :
                declaration.Storage == GlInterfaceStorage.Input && stage == StageRole.Vertex ? null : contract.VaryingLocations;
            bool hasSourceLocation = layouts.Any(l => l.Qualifiers.Any(q => q.Kind == GlLayoutQualifierKind.Location));
            if (locations != null && !(attachment && hasSourceLocation) && locations.TryGetValue(name, out int location))
                values.Add(GlLayoutQualifierKind.Location, location);
        }
        if (values.Count == 0) return;
        if (layouts.Count == 0)
        {
            editor.InsertBefore(insertionPoint, GlLayoutNode.Format(values));
            return;
        }

        // Keep untouched entries as syntax, so nested expressions and comments containing
        // commas or equals signs cannot be mistaken for layout separators.
        for (int i = 0; i < layouts.Count; i++)
        {
            var retained = layouts[i].Qualifiers.Where(q => !values.ContainsKey(q.Kind)).Select(q => q.ToText()).ToList();
            if (i == 0) retained.Add(GlLayoutNode.FormatAssignments(values));
            if (retained.Count == 0) editor.Remove(layouts[i]);
            else editor.Replace(layouts[i].Arguments, "(" + string.Join(", ", retained) + ")");
        }
    }
    #endregion
}
