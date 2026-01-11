using System;
using System.Collections.Generic;
using System.IO;
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
    private static readonly string NormalizationRoot = EnsureTrailingSeparator(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "vge-tests-preprocessor-root")));

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

        // Resolve like production: domain:path stays absolute; everything else is relative
        // to the importing file's directory (relativeTo).
        (string domain, string key) = ResolveReference(reference, relativeTo);

        // For lookup, we only use the normalized path portion.
        // Be tolerant for legacy tests: allow looking up just the filename and the old shaderincludes/ prefix.
        string[] candidates = (!key.Contains('/'))
            ? [key, $"shaders/includes/{key}", $"shaderincludes/{key}"]
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

        var id = new ResourceId($"{domain}:{matchedKey}");
        var tree = SyntaxTree.ParseAndBind(text, GlslSchema.Instance);
        var resource = new Resource<SyntaxTree>(id, tree, EmptyMetadata);

        return ValueTask.FromResult(ResourceResolutionResult<SyntaxTree>.Success(resource));
    }

    private (string Domain, string Path) ResolveReference(string reference, IResource<SyntaxTree>? relativeTo)
    {
        reference = (reference ?? string.Empty).Trim();
        reference = reference.Replace('\\', '/');

        // Absolute reference: domain:path
        if (reference.Contains(':'))
        {
            var (domain, path) = SplitDomainAndPath(reference);
            return (domain, NormalizePathWithinDomain(path));
        }

        // Relative reference: resolve against importing file path.
        var baseIdPath = relativeTo?.Id.Path ?? string.Empty;
        var (baseDomain, basePath) = SplitDomainAndPath(baseIdPath);
        var resolvedPath = ResolveRelativePath(reference, basePath);

        return (baseDomain, NormalizePathWithinDomain(resolvedPath));
    }

    private (string Domain, string Path) SplitDomainAndPath(string domainAndPath)
    {
        if (string.IsNullOrWhiteSpace(domainAndPath))
        {
            return (_defaultDomain, string.Empty);
        }

        var trimmed = domainAndPath.Trim();
        int sepIndex = trimmed.IndexOf(':');
        if (sepIndex < 0)
        {
            return (_defaultDomain, trimmed.Replace('\\', '/'));
        }

        string domain = trimmed[..sepIndex];
        string path = trimmed[(sepIndex + 1)..];

        if (string.IsNullOrWhiteSpace(domain))
        {
            domain = _defaultDomain;
        }

        return (domain, path.Replace('\\', '/'));
    }

    private static string ResolveRelativePath(string reference, string basePath)
    {
        reference = (reference ?? string.Empty).Trim();
        reference = reference.Replace('\\', '/');

        // Defensive: basePath might accidentally be domain:path.
        int sepIndex = basePath.IndexOf(':');
        if (sepIndex >= 0)
        {
            basePath = basePath[(sepIndex + 1)..];
        }

        string basePathOs = (basePath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
        string? baseDirOs = Path.GetDirectoryName(basePathOs);
        baseDirOs ??= string.Empty;

        string relativeOs = reference.Replace('/', Path.DirectorySeparatorChar);

        string combinedFull = Path.GetFullPath(Path.Combine(NormalizationRoot, baseDirOs, relativeOs));
        string normalizedRelative = Path.GetRelativePath(NormalizationRoot, combinedFull);

        return NormalizeSeparators(normalizedRelative);
    }

    private static string NormalizePathWithinDomain(string path)
    {
        path = (path ?? string.Empty).Replace('\\', '/');
        string pathOs = path.Replace('/', Path.DirectorySeparatorChar);

        string full = Path.GetFullPath(Path.Combine(NormalizationRoot, pathOs));
        string rel = Path.GetRelativePath(NormalizationRoot, full);

        return NormalizeSeparators(rel);
    }

    private static string NormalizeSeparators(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar))
        {
            return path;
        }
        return path + Path.DirectorySeparatorChar;
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
