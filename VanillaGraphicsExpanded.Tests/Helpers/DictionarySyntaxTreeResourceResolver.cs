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

        reference = (reference ?? string.Empty).Trim();
        reference = RemoveControlChars(reference);
        reference = reference.Replace('\\', '/');

        if (string.IsNullOrWhiteSpace(reference))
        {
            var diag = new ResolutionFailedDiagnostic(reference ?? string.Empty, "Empty import reference", relativeTo?.Id, null);
            return ValueTask.FromResult(ResourceResolutionResult<SyntaxTree>.Failure(diag));
        }

        // The preprocessor may already qualify references (e.g. "domain:path").
        // For lookup, we always use the path portion.
        string key = reference;
        int sepIndex = key.IndexOf(':');
        if (sepIndex >= 0)
        {
            key = key[(sepIndex + 1)..];
        }

        // Mirror production: bare filenames resolve from shaderincludes/ in the default domain.
        // Be tolerant: try both the raw key and the shaderincludes/ prefixed key.
        string[] candidates = (!key.Contains('/'))
            ? [key, $"shaderincludes/{key}"]
            : [key];

        string? text = null;
        string? matchedKey = null;
        foreach (var candidate in candidates)
        {
            if (_sources.TryGetValue(candidate, out text))
            {
                matchedKey = candidate;
                break;
            }
        }

        if (text is null || matchedKey is null)
        {
            string candidatesText = string.Join(", ", candidates);
            string failure = $"Test include not found. key='{key}' candidates=[{candidatesText}]";

            var diag = new ResolutionFailedDiagnostic(
                reference,
                failure,
                relativeTo?.Id,
                null);
            return ValueTask.FromResult(ResourceResolutionResult<SyntaxTree>.Failure(diag));
        }

        var id = new ResourceId($"{_defaultDomain}:{matchedKey}");
        var tree = SyntaxTree.ParseAndBind(text, GlslSchema.Instance);
        var resource = new Resource<SyntaxTree>(id, tree, EmptyMetadata);

        return ValueTask.FromResult(ResourceResolutionResult<SyntaxTree>.Success(resource));
    }

    private static string RemoveControlChars(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        Span<char> buffer = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        int idx = 0;
        foreach (char c in value)
        {
            if (!char.IsControl(c))
            {
                buffer[idx++] = c;
            }
        }

        return new string(buffer[..idx]);
    }
}

#endregion
