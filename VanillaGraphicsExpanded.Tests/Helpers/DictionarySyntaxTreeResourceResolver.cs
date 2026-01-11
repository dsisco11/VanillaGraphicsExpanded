using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using TinyPreprocessor.Core;
using TinyPreprocessor.Diagnostics;

using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.Tests.Helpers;

#region Dictionary-backed SyntaxTree Resolver

/// <summary>
/// Simple resolver for tests: resolves import references from an in-memory map of file name to source text.
/// </summary>
public sealed class DictionarySyntaxTreeResourceResolver : IResourceResolver<SyntaxTree>
{
    private static readonly IReadOnlyDictionary<string, object> EmptyMetadata = new Dictionary<string, object>();

    private readonly IReadOnlyDictionary<string, string> _sources;
    private readonly string _defaultDomain;

    public DictionarySyntaxTreeResourceResolver(IReadOnlyDictionary<string, string> sources, string defaultDomain)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultDomain);

        _sources = sources;
        _defaultDomain = defaultDomain;
    }

    public ValueTask<ResourceResolutionResult<SyntaxTree>> ResolveAsync(
        string reference,
        IResource<SyntaxTree>? relativeTo,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(reference))
        {
            var diag = new ResolutionFailedDiagnostic(reference ?? string.Empty, "Empty import reference", relativeTo?.Id, null);
            return ValueTask.FromResult(ResourceResolutionResult<SyntaxTree>.Failure(diag));
        }

        // Mirror production: bare filenames resolve from shaderincludes/ in the default domain.
        string key = reference;
        if (!reference.Contains('/') && !reference.Contains(':'))
        {
            key = $"shaderincludes/{reference}";
        }

        if (!_sources.TryGetValue(key, out var text))
        {
            var diag = new ResolutionFailedDiagnostic(reference, $"Test include not found: {key}", relativeTo?.Id, null);
            return ValueTask.FromResult(ResourceResolutionResult<SyntaxTree>.Failure(diag));
        }

        var id = new ResourceId($"{_defaultDomain}:{key}");
        var tree = SyntaxTree.Parse(text, GlslSchema.Instance);
        var resource = new Resource<SyntaxTree>(id, tree, EmptyMetadata);

        return ValueTask.FromResult(ResourceResolutionResult<SyntaxTree>.Success(resource));
    }
}

#endregion
