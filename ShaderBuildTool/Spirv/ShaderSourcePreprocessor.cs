using System.Collections.Immutable;
using TinyPreprocessor.Core;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded;


namespace ShaderBuildTool.Spirv;

/// <summary>Processes offline shader imports through the same syntax-tree pipeline as runtime sources.</summary>
internal sealed class ShaderSourcePreprocessor
{
    private readonly string root;
    private readonly string domain;

    #region Public API
    /// <summary>Identifies the asset tree used for stage sources and imported resources.</summary>
    public ShaderSourcePreprocessor(string assetsRoot, string assetDomain)
    {
        root = Path.GetFullPath(assetsRoot);
        domain = assetDomain;
    }

    /// <summary>Returns the same canonical resource identity used when processing a root.</summary>
    internal string RootId(string relative)
    {
        string sourceRoot = Path.Combine(root, domain, "shaders");
        string path = Path.GetFullPath(Path.Combine(sourceRoot, relative));
        if (!path.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Shader source escaped its asset directory: " + relative);
        return $"{domain}:shaders/{Path.GetRelativePath(sourceRoot, path).Replace('\\', '/')}";
    }

    /// <summary>Expands syntax imports using library-owned dependency ordering and source attribution.</summary>
    public ShaderExpandedSource Expand(string relative)
    {

        var id = new ResourceId(RootId(relative));
        string path = Path.GetFullPath(Path.Combine(root, domain, "shaders", relative));
        relative = Path.GetRelativePath(Path.Combine(root, domain, "shaders"), path).Replace('\\', '/');
        var sources = new Dictionary<ResourceId, SyntaxTree>();
        var (raw, rootInput) = ShaderInputObservation.Read(id.Path, path);
        var inputs = new Dictionary<string, ShaderInputObservation>(StringComparer.Ordinal) { [id.Path] = rootInput };
        var parsed = SyntaxTree.Parse(raw, GlslSchema.Instance);
        sources.Add(id, parsed);
        var resolver = new FileSystemSyntaxTreeResourceResolver(root, domain, (resourceId, file, text, tree) =>
        {
            sources[resourceId] = tree;
        }, observation =>
        {
            if (inputs.TryGetValue(observation.Resource, out var previous) && previous != observation)
                throw new IOException("Shader resource changed during preprocessing: " + observation.Resource);
            inputs[observation.Resource] = observation;
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
        // Numerical fixtures exercise the exact terrain AST transformation before binary compilation.
        if (relative is "tests/terrain-capture-opaque.fsh" or "tests/terrain-capture-topsoil.fsh")
            VanillaGraphicsExpanded.PBR.PbrTerrainColorPatches.ApplyFragment(output,
                relative.EndsWith("opaque.fsh", StringComparison.Ordinal) ? "chunkopaque.fsh" : "chunktopsoil.fsh");
        if (result.DependencyGraph is null) throw new InvalidDataException("Successful preprocessing omitted dependency graph.");
        var expanded = new ShaderExpandedSource(id.Path, root, SourceCodeImportsProcessor.StripNonAscii(output.ToText()),
            ShaderDependencySnapshot.Capture(result.DependencyGraph, result.ProcessedResources),
            inputs.Values.OrderBy(input => input.Resource, StringComparer.Ordinal).ToImmutableArray());
        ShaderExpandedSource.Validate(expanded);
        return expanded;
    }
    #endregion
}
