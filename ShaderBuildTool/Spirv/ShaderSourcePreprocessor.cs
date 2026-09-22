using TinyPreprocessor.Core;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded;


namespace ShaderBuildTool.Spirv;

/// <summary>Processes offline shader imports through the same syntax-tree pipeline as runtime sources.</summary>
internal sealed class ShaderSourcePreprocessor
{
    private readonly string root;
    private readonly string domain;

    #region Construction
    /// <summary>Identifies the asset tree used for stage sources and imported resources.</summary>
    public ShaderSourcePreprocessor(string assetsRoot, string assetDomain)
    {
        root = Path.GetFullPath(assetsRoot);
        domain = assetDomain;
    }
    #endregion

    #region Preprocessing
    /// <summary>Expands syntax imports using library-owned dependency ordering and source attribution.</summary>
    public string Expand(string relative)
    {

        string sourceRoot = Path.Combine(root, domain, "shaders");
        string path = Path.GetFullPath(Path.Combine(sourceRoot, relative));
        if (!path.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Shader source escaped its asset directory: " + relative);
        string raw = File.ReadAllText(path);
        var sources = new Dictionary<ResourceId, SyntaxTree>();
        var id = new ResourceId($"{domain}:shaders/{relative.Replace('\\', '/')}");
        var parsed = SyntaxTree.Parse(raw, GlslSchema.Instance);
        sources.Add(id, parsed);
        var resolver = new FileSystemSyntaxTreeResourceResolver(root, domain, (resourceId, file, text, tree) =>
        {
            sources[resourceId] = tree;
        });
        var result = new ShaderSyntaxTreePreprocessor(resolver).Process(id, parsed);
        if (!result.Success)
            throw new InvalidOperationException($"GLSL preprocessing failed for '{relative}':\n" +
                string.Join("\n", result.Diagnostics.Select(d => d.ToString())));

        // Preserve the shared preprocessor's source attribution for compiler diagnostics.
        var output = result.Content;
        if (result.SourceMap is not null)
        {
            var mapped = LineDirectiveInjector.TryInject(output, result.SourceMap, resourceId => sources[resourceId]);
            if (mapped.Success) output = mapped.OutputTree;
        }
        return SourceCodeImportsProcessor.StripNonAscii(output.ToText());
    }
    #endregion
}
