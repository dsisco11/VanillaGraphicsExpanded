using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using TinyPreprocessor.Core;
using TinyPreprocessor.Diagnostics;

using TinyTokenizer.Ast;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded;

#region Asset-backed SyntaxTree Resolver

/// <summary>
/// Resolves <c>@import</c> references by loading shader include files through Vintage Story's asset system
/// and parsing them into schema-bound <see cref="SyntaxTree"/> instances.
/// </summary>
public sealed class AssetSyntaxTreeResourceResolver : IResourceResolver<SyntaxTree>
{
    private static readonly IReadOnlyDictionary<string, object> EmptyMetadata = new Dictionary<string, object>();
    private static readonly string NormalizationRoot = EnsureTrailingSeparator(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "vge-preprocessor-root")));

    private readonly IAssetManager _assets;
    private readonly string _defaultDomain;

    public AssetSyntaxTreeResourceResolver(IAssetManager assets, string defaultDomain)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultDomain);

        _assets = assets;
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

        if (string.IsNullOrWhiteSpace(reference))
        {
            var diag = new ResolutionFailedDiagnostic(reference ?? string.Empty, "Empty import reference", relativeTo?.Id, null);
            return ValueTask.FromResult(ResourceResolutionResult<SyntaxTree>.Failure(diag));
        }

        string resolvedIdPath;
        try
        {
            resolvedIdPath = ResolveResourceIdPath(reference, relativeTo);
        }
        catch (Exception ex)
        {
            var diag = new ResolutionFailedDiagnostic(reference, ex.Message, relativeTo?.Id, null);
            return ValueTask.FromResult(ResourceResolutionResult<SyntaxTree>.Failure(diag));
        }

        var id = new ResourceId(resolvedIdPath);
        var location = AssetLocation.Create(resolvedIdPath, _defaultDomain);

        IAsset? asset = _assets.TryGet(location, loadAsset: true);
        if (asset is null)
        {
            var diag = new ResolutionFailedDiagnostic(reference, $"Asset not found: {location}", relativeTo?.Id, null);
            return ValueTask.FromResult(ResourceResolutionResult<SyntaxTree>.Failure(diag));
        }

        string text = asset.ToText();
        if (string.IsNullOrEmpty(text))
        {
            var diag = new ResolutionFailedDiagnostic(reference, $"Asset was empty: {location}", relativeTo?.Id, null);
            return ValueTask.FromResult(ResourceResolutionResult<SyntaxTree>.Failure(diag));
        }

        var tree = SyntaxTree.Parse(text, GlslSchema.Instance);
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

    private string ResolveResourceIdPath(string reference, IResource<SyntaxTree>? relativeTo)
    {
        // Normalize separators (TinyPreprocessor paths are opaque strings; we normalize for AssetLocation compatibility)
        reference = reference.Replace('\\', '/');

        // If reference is a bare filename, keep legacy behavior: resolve from shaderincludes/.
        if (!reference.Contains('/') && !reference.Contains(':'))
        {
            return $"{_defaultDomain}:shaderincludes/{reference}";
        }

        // Absolute domain:path
        if (reference.Contains(':'))
        {
            var (domain, path) = SplitDomainAndPath(reference);
            return $"{domain}:{NormalizePathWithinDomain(path)}";
        }

        // Relative reference (./ or ../)
        if ((reference.StartsWith("./", StringComparison.Ordinal) || reference.StartsWith("../", StringComparison.Ordinal))
            && relativeTo is not null)
        {
            return ResolveRelativePath(reference, relativeTo.Id.Path);
        }

        // Domain-less explicit path (default to mod domain)
        return $"{_defaultDomain}:{NormalizePathWithinDomain(reference)}";
    }

    private string ResolveRelativePath(string relativePath, string baseResourceIdPath)
    {
        // baseResourceIdPath uses domain:path. Split and normalize relative to the base directory of the path component.
        var (baseDomain, basePath) = SplitDomainAndPath(baseResourceIdPath, _defaultDomain);

        // Normalize separators for System.IO.Path.
        string basePathOs = basePath.Replace('/', Path.DirectorySeparatorChar);
        string? baseDirOs = Path.GetDirectoryName(basePathOs);
        baseDirOs ??= string.Empty;

        string relativeOs = relativePath.Replace('/', Path.DirectorySeparatorChar);

        string combinedFull = Path.GetFullPath(Path.Combine(NormalizationRoot, baseDirOs, relativeOs));
        string normalizedRelative = Path.GetRelativePath(NormalizationRoot, combinedFull);

        return $"{baseDomain}:{NormalizeSeparators(normalizedRelative)}";
    }

    private static string NormalizePathWithinDomain(string path)
    {
        path = path.Replace('\\', '/');
        string pathOs = path.Replace('/', Path.DirectorySeparatorChar);

        string full = Path.GetFullPath(Path.Combine(NormalizationRoot, pathOs));
        string rel = Path.GetRelativePath(NormalizationRoot, full);

        return NormalizeSeparators(rel);
    }

    private static (string Domain, string Path) SplitDomainAndPath(string domainAndPath, string? defaultDomain = null)
    {
        int sepIndex = domainAndPath.IndexOf(':');
        if (sepIndex < 0)
        {
            return (defaultDomain ?? string.Empty, domainAndPath);
        }

        string domain = domainAndPath[..sepIndex];
        string path = domainAndPath[(sepIndex + 1)..];
        return (domain, path);
    }

    private static string NormalizeSeparators(string path)
    {
        // AssetLocation expects forward slashes.
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
}

#endregion
