using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShaderBuildTool.Spirv;

/// <summary>Persists enough owned state to distinguish interrupted generation and receipt transitions.</summary>
internal sealed record ShaderPublicationJournal(int Version, string Id, string Active, string Pending, string Previous,
    string Fingerprint, ShaderPublicationState State, ShaderGenerationSnapshot? Prior, bool PriorCoherent, ShaderGenerationSnapshot? Next)
{
    private static readonly JsonSerializerOptions JournalJson = new()
    {
        Converters = { new JsonStringEnumConverter<ShaderPublicationState>(allowIntegerValues: false) }
    };

    /// <summary>Detects partial or accidentally damaged journal metadata before recovery interprets it.</summary>
    private sealed record Envelope(byte[] Payload, string Digest);

    #region Public API
    /// <summary>Validates every path against independently derived ownership before reading or deleting generations.</summary>
    internal void Validate(ShaderPublicationPaths paths)
    {
        if (Version != 1 || Active != paths.Active || Pending != paths.Transaction(Id, false) || Previous != paths.Transaction(Id, true)
            || Fingerprint is null || !Enum.IsDefined(State)
            || State != ShaderPublicationState.Staging && Next is null || PriorCoherent && Prior is null)
            throw new InvalidDataException("Publication journal has incompatible or unowned state.");
        ShaderPublicationPaths.EnsureUnredirected(Active);
        ShaderPublicationPaths.EnsureUnredirected(Pending);
        ShaderPublicationPaths.EnsureUnredirected(Previous);
        Prior?.Validate(Previous);
        Next?.Validate(Pending);
    }

    /// <summary>Publishes state by replacement so an interrupted write leaves the preceding complete journal.</summary>
    internal void Save(ShaderPublicationPaths paths)
    {
        Validate(paths);
        ShaderPublicationPaths.EnsureUnredirected(paths.Journal);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Journal)!);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(this, JournalJson);
        string pending = paths.Journal + ".pending";
        ShaderPublicationPaths.EnsureUnredirected(pending);
        try
        {
            File.WriteAllBytes(pending, JsonSerializer.SerializeToUtf8Bytes(new Envelope(payload, ShaderRecordStore.Digest(payload))));
            File.Move(pending, paths.Journal, true);
        }
        finally { File.Delete(pending); }
    }

    /// <summary>Reads only complete journal data with matching payload integrity and owned references.</summary>
    internal static ShaderPublicationJournal Read(ShaderPublicationPaths paths)
    {
        ShaderPublicationPaths.EnsureUnredirected(paths.Journal);
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(paths.Journal));
            if (envelope is not { Payload: not null } || ShaderRecordStore.Digest(envelope.Payload) != envelope.Digest)
                throw new InvalidDataException("Publication journal digest is invalid.");
            var journal = JsonSerializer.Deserialize<ShaderPublicationJournal>(envelope.Payload, JournalJson)
                ?? throw new InvalidDataException("Publication journal is empty.");
            journal.Validate(paths);
            return journal;
        }
        catch (JsonException ex) { throw new InvalidDataException("Publication journal is malformed; inspect and repair the output tree before rebuilding.", ex); }
    }
    #endregion
}
