using System.Text.Json;
using System.Text.Json.Nodes;
using ShaderBuildTool.Spirv;

namespace ShaderBuildTool.Tests;

/// <summary>Guards typed recovery-state persistence and rejects malformed state values.</summary>
public sealed class ShaderPublicationJournalTests
{
    #region Public API
    /// <summary>Typed state values preserve the existing journal names and round-trip through validated storage.</summary>
    [Theory]
    [InlineData("Staging")]
    [InlineData("Prepared")]
    [InlineData("Installed")]
    [InlineData("Committed")]
    public void StateNamesRoundTrip(string name)
    {
        using var fixture = new ShaderBuildFixture();
        var paths = new ShaderPublicationPaths(fixture.Output, "test");
        var state = Enum.Parse<ShaderPublicationState>(name);
        var journal = Create(paths, state);
        journal.Save(paths);
        var envelope = JsonNode.Parse(File.ReadAllBytes(paths.Journal))!;
        var payload = JsonNode.Parse(Convert.FromBase64String(envelope["Payload"]!.GetValue<string>()))!;
        Assert.Equal(name, payload["State"]!.GetValue<string>());
        Assert.Equal(state, ShaderPublicationJournal.Read(paths).State);
    }

    /// <summary>Even correctly checksummed payloads cannot use unknown, numeric, or absent recovery states.</summary>
    [Theory]
    [InlineData("\"Unknown\"")]
    [InlineData("1")]
    [InlineData("\"1\"")]
    [InlineData(null)]
    public void InvalidStateIsRejected(string? stateJson)
    {
        using var fixture = new ShaderBuildFixture();
        var paths = new ShaderPublicationPaths(fixture.Output, "test");
        Create(paths, ShaderPublicationState.Staging).Save(paths);
        var envelope = JsonNode.Parse(File.ReadAllBytes(paths.Journal))!;
        var payload = JsonNode.Parse(Convert.FromBase64String(envelope["Payload"]!.GetValue<string>()))!.AsObject();
        // Recompute envelope integrity so the state contract itself must reject the tampered value.
        if (stateJson is null) payload.Remove("State");
        else payload["State"] = JsonNode.Parse(stateJson);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        File.WriteAllBytes(paths.Journal, JsonSerializer.SerializeToUtf8Bytes(new { Payload = bytes, Digest = ShaderRecordStore.Digest(bytes) }));
        Assert.Throws<InvalidDataException>(() => ShaderPublicationJournal.Read(paths));
    }
    #endregion

    #region Private
    /// <summary>Creates valid path ownership and snapshot metadata independent of any installed generation.</summary>
    private static ShaderPublicationJournal Create(ShaderPublicationPaths paths, ShaderPublicationState state)
    {
        string id = Guid.NewGuid().ToString("N");
        return new(1, id, paths.Active, paths.Transaction(id, false), paths.Transaction(id, true), "fixture", state,
            null, false, state == ShaderPublicationState.Staging ? null : new ShaderGenerationSnapshot(new()));
    }
    #endregion
}
