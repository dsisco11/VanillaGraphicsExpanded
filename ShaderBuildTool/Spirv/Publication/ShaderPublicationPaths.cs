namespace ShaderBuildTool.Spirv;

/// <summary>Constrains publication, journal and transaction paths to the leased output tree.</summary>
internal sealed class ShaderPublicationPaths
{
    internal string Output { get; }
    internal string Active { get; }
    internal string Journal { get; }
    internal string Receipt => Path.Combine(Output, "build-receipt.json");

    #region Public API
    /// <summary>Resolves a single asset domain and rejects redirected ownership before mutation.</summary>
    internal ShaderPublicationPaths(string outputRoot, string domain)
    {
        if (string.IsNullOrWhiteSpace(domain) || domain.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-'))
            throw new ArgumentException("Publication domain must be one asset directory.", nameof(domain));
        Output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        Active = Path.Combine(Output, domain, "shaders");
        Journal = Path.Combine(Output, "_tmp", "publication.json");
        EnsureUnredirected(Active);
        EnsureUnredirected(Journal);
    }

    /// <summary>Derives a reserved sibling directory from a validated transaction identifier.</summary>
    internal string Transaction(string id, bool previous)
    {
        if (!Guid.TryParseExact(id, "N", out var parsed) || parsed.ToString("N") != id)
            throw new InvalidDataException("Invalid shader publication transaction identity.");
        return Path.Combine(Path.GetDirectoryName(Active)!, (previous ? ".shader-previous-" : ".shader-pending-") + id);
    }

    /// <summary>Accepts canonical relative file names and rejects redirection at every existing ancestor.</summary>
    internal static string FileWithin(string directory, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new InvalidDataException("Invalid publication file reference.");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || Path.GetRelativePath(root, path) != relative)
            throw new InvalidDataException("Publication file escapes its owned directory: " + relative);
        EnsureUnredirected(path);
        return path;
    }

    /// <summary>Checks existing paths without following filesystem links outside the owned tree.</summary>
    internal static void EnsureUnredirected(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Redirected shader publication path is not owned: " + current);
    }

    /// <summary>Deletes only a derived transaction directory after checking all descendants for redirection.</summary>
    internal void DeleteTransaction(string id, bool previous)
    {
        string path = Transaction(id, previous);
        EnsureUnredirected(path);
        if (!Directory.Exists(path)) return;
        ShaderGenerationSnapshot.Files(path).ToArray();
        Directory.Delete(path, recursive: true);
    }
    #endregion
}
