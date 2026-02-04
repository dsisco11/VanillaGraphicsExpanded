using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using TinyPreprocessor.Core;
using TinyPreprocessor.Diagnostics;
using TinyTokenizer.Ast;

using VanillaGraphicsExpanded;

namespace ShaderBuildTool.Spirv;

/// <summary>
/// Resolves <c>@import</c> references by reading shader include files from disk under a Vintage Story assets tree.
///
/// Expected layout:
/// - {assetsRoot}/{domain}/{path}
///
/// Example:
/// - assetsRoot = "../VanillaGraphicsExpanded/assets"
/// - id = "vanillagraphicsexpanded:shaders/includes/foo.glsl"
/// - file = "../VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/includes/foo.glsl"
/// </summary>
internal sealed class FileSystemSyntaxTreeResourceResolver : IResourceResolver<SyntaxTree>
{
    private static readonly IReadOnlyDictionary<string, object> EmptyMetadata = new Dictionary<string, object>();
    private static readonly string NormalizationRoot = EnsureTrailingSeparator(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "vge-spirv-preprocessor-root")));

    private readonly string assetsRoot;
    private readonly string defaultDomain;

    public FileSystemSyntaxTreeResourceResolver(string assetsRoot, string defaultDomain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultDomain);

        this.assetsRoot = assetsRoot;
        this.defaultDomain = defaultDomain;
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
        var (domain, path) = SplitDomainAndPath(resolvedIdPath);

        string filePath = Path.Combine(assetsRoot, domain, path.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(filePath))
        {
            var diag = new ResolutionFailedDiagnostic(reference, $"File not found: {filePath}", relativeTo?.Id, null);
            return ValueTask.FromResult(ResourceResolutionResult<SyntaxTree>.Failure(diag));
        }

        string text = File.ReadAllText(filePath);
        if (string.IsNullOrEmpty(text))
        {
            var diag = new ResolutionFailedDiagnostic(reference, $"File was empty: {filePath}", relativeTo?.Id, null);
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
        reference = reference.Replace('\\', '/');

        if (reference.Contains(':'))
        {
            var (domain, path) = SplitDomainAndPath(reference);
            return $"{domain}:{NormalizePathWithinDomain(path)}";
        }

        string baseIdPath = relativeTo?.Id.Path ?? string.Empty;
        var (baseDomain, basePath) = SplitDomainAndPath(baseIdPath);

        string resolvedPath = ResolveResourcePath(reference, basePath);
        return $"{baseDomain}:{NormalizePathWithinDomain(resolvedPath)}";
    }

    private string ResolveResourcePath(string reference, string basePath)
    {
        reference = (reference ?? string.Empty).Trim();
        reference = reference.Replace('\\', '/');

        (_, basePath) = SplitDomainAndPath(basePath);

        string basePathOs = (basePath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
        string? baseDirOs = Path.GetDirectoryName(basePathOs);
        baseDirOs ??= string.Empty;

        string relativeOs = reference.Replace('/', Path.DirectorySeparatorChar);

        string combinedFull = Path.GetFullPath(Path.Combine(NormalizationRoot, baseDirOs, relativeOs));
        string normalizedRelative = Path.GetRelativePath(NormalizationRoot, combinedFull);

        return NormalizeSeparators(normalizedRelative);
    }

    private (string Domain, string Path) SplitDomainAndPath(string domainAndPath)
    {
        if (string.IsNullOrWhiteSpace(domainAndPath))
        {
            return (defaultDomain, string.Empty);
        }

        int colon = domainAndPath.IndexOf(':');
        if (colon < 0)
        {
            return (defaultDomain, domainAndPath);
        }

        string domain = domainAndPath[..colon];
        string path = domainAndPath[(colon + 1)..];
        if (string.IsNullOrWhiteSpace(domain))
        {
            domain = defaultDomain;
        }

        return (domain, path);
    }

    private static string NormalizePathWithinDomain(string path)
    {
        path = path.Replace('\\', '/');
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
}
