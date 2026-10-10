using ShaderBuildTool.Spirv;
using System.Text.Json;
using System.Text.Json.Nodes;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Tests;

/// <summary>Exercises recoverable generations and immutable output reuse on the host filesystem.</summary>
public sealed class ShaderPublicationTests
{
    #region Public API
    /// <summary>Each durable transition recovers a complete generation and never invents success.</summary>
    [Theory]
    [InlineData((int)ShaderPublicationPoint.Staging)]
    [InlineData((int)ShaderPublicationPoint.Staged)]
    [InlineData((int)ShaderPublicationPoint.Prepared)]
    [InlineData((int)ShaderPublicationPoint.PreviousMoved)]
    [InlineData((int)ShaderPublicationPoint.Installed)]
    [InlineData((int)ShaderPublicationPoint.BeforeReceipt)]
    [InlineData((int)ShaderPublicationPoint.ReceiptPublished)]
    [InlineData((int)ShaderPublicationPoint.Cleanup)]
    public void InterruptedPublicationRecoversCoherentGeneration(int boundary)
    {
        var point = (ShaderPublicationPoint)boundary;
        using var fixture = new PublicationFixture();
        fixture.Publish(fixture.Old, "old");
        using var lease = ShaderOutputLease.Acquire(fixture.Output);
        var publisher = new ShaderPublication(fixture.Output, "test", checkpoint: current =>
        {
            if (current == point) throw new IOException("Injected interruption");
        }, recoverOnFailure: false);
        Assert.Throws<IOException>(() => fixture.Publish(fixture.New, "new", publisher));
        new ShaderPublication(fixture.Output, "test").Recover();
        Assert.True(fixture.Matches(fixture.Old) || fixture.Matches(fixture.New));
        bool committed = point is ShaderPublicationPoint.ReceiptPublished or ShaderPublicationPoint.Cleanup;
        Assert.Equal(committed, ShaderBuildReceipt.IsCurrent(fixture.Output, "new"));
        Assert.False(ShaderBuildReceipt.IsCurrent(fixture.Output, "old"));
        Assert.Empty(Directory.GetDirectories(Path.Combine(fixture.Output, "test"), ".shader-*"));
    }

    /// <summary>Actual same-volume hard links share identity while replacement leaves old inode contents intact.</summary>
    [Fact]
    public void HostHardLinksReuseImmutableFiles()
    {
        using var fixture = new PublicationFixture();
        fixture.Publish(fixture.Old, "old");
        File.SetLastWriteTimeUtc(fixture.Binary("same.spv"), DateTime.UnixEpoch);
        var result = fixture.Publish(fixture.New, "new");
        if (OperatingSystem.IsWindows()) Assert.True(result.Linked > 0, "Expected NTFS same-volume hard-link support.");
        Assert.Equal(DateTime.UnixEpoch, File.GetLastWriteTimeUtc(fixture.Binary("same.spv")));
        Assert.True(fixture.Matches(fixture.New));
        var replaced = new Dictionary<string, byte[]>(fixture.New) { ["same.spv"] = [9, 8] };
        fixture.Publish(replaced, "replaced");
        Assert.True(fixture.Matches(replaced));
    }

    /// <summary>Replacing a cache-owned binary cannot mutate an already linked generation.</summary>
    [Fact]
    public void AtomicBinaryReplacementPreservesExistingHardLinkContents()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new PublicationFixture();
        string source = Path.Combine(fixture.Output, "source.bin");
        string linked = Path.Combine(fixture.Output, "linked.bin");
        File.WriteAllBytes(source, [1, 2, 3]);
        Assert.True(ShaderBinaryReuse.TryLink(linked, source));
        ShaderVariantCache.Publish(source, [9, 8, 7]);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(linked));
        Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(source));
    }

    /// <summary>A cancelled first publication cannot create a successful catalogue.</summary>
    [Theory]
    [InlineData((int)ShaderPublicationPoint.Staging)]
    [InlineData((int)ShaderPublicationPoint.Prepared)]
    [InlineData((int)ShaderPublicationPoint.PreviousMoved)]
    [InlineData((int)ShaderPublicationPoint.Installed)]
    public void FirstPublicationInterruptionNeverClaimsSuccess(int boundary)
    {
        using var fixture = new PublicationFixture();
        var publisher = new ShaderPublication(fixture.Output, "test", checkpoint: point =>
        {
            if ((int)point == boundary) throw new OperationCanceledException();
        }, recoverOnFailure: false);
        Assert.Throws<OperationCanceledException>(() => fixture.Publish(fixture.New, "new", publisher));
        new ShaderPublication(fixture.Output, "test").Recover();
        Assert.True(!Directory.Exists(fixture.Active) || fixture.Matches(fixture.New));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
    }

    /// <summary>Untrusted recovery state cannot authorize deleting unrelated filesystem content.</summary>
    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Version\":1,\"Pending\":\"C:/\",\"Previous\":\"../outside\"}")]
    public void InvalidJournalFailsClosed(string journal)
    {
        using var fixture = new PublicationFixture();
        fixture.Publish(fixture.Old, "old");
        string scratch = Path.Combine(fixture.Output, "_tmp");
        Directory.CreateDirectory(scratch);
        File.WriteAllText(Path.Combine(scratch, "publication.json"), journal);
        Assert.ThrowsAny<Exception>(() => new ShaderPublication(fixture.Output, "test").Recover());
        Assert.True(fixture.Matches(fixture.Old));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
    }

    /// <summary>A checksummed but redirected journal cannot remove the referenced foreign directory.</summary>
    [Fact]
    public void ValidEnvelopeWithForeignPathIsRejected()
    {
        using var fixture = new PublicationFixture();
        fixture.Publish(fixture.Old, "old");
        var publisher = new ShaderPublication(fixture.Output, "test", checkpoint: point =>
        {
            if (point == ShaderPublicationPoint.Prepared) throw new IOException("stop");
        }, recoverOnFailure: false);
        Assert.Throws<IOException>(() => fixture.Publish(fixture.New, "new", publisher));
        string foreign = Path.Combine(fixture.Root, "foreign");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "sentinel"), "keep");
        string path = Path.Combine(fixture.Output, "_tmp", "publication.json");
        var envelope = JsonNode.Parse(File.ReadAllBytes(path))!;
        var payload = JsonNode.Parse(Convert.FromBase64String(envelope["Payload"]!.GetValue<string>()))!;
        payload["Pending"] = foreign;
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new { Payload = bytes, Digest = ShaderRecordStore.Digest(bytes) }));
        Assert.Throws<InvalidDataException>(() => new ShaderPublication(fixture.Output, "test").Recover());
        Assert.Equal("keep", File.ReadAllText(Path.Combine(foreign, "sentinel")));
        Assert.True(fixture.Matches(fixture.Old));
    }

    /// <summary>Damaged installed content rolls back to the still-verified prior generation.</summary>
    [Fact]
    public void CorruptInstalledGenerationRecoversPrevious()
    {
        using var fixture = new PublicationFixture();
        fixture.Publish(fixture.Old, "old");
        var publisher = new ShaderPublication(fixture.Output, "test", checkpoint: point =>
        {
            if (point == ShaderPublicationPoint.Installed) throw new IOException("stop");
        }, recoverOnFailure: false);
        Assert.Throws<IOException>(() => fixture.Publish(fixture.New, "new", publisher));
        File.WriteAllBytes(fixture.Binary("change.spv"), [0]);
        new ShaderPublication(fixture.Output, "test").Recover();
        Assert.True(fixture.Matches(fixture.Old));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
    }

    /// <summary>An already cancelled invocation retains its prior coherent generation without success.</summary>
    [Fact]
    public void CancelledPublicationLeavesPriorGeneration()
    {
        using var fixture = new PublicationFixture();
        fixture.Publish(fixture.Old, "old");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => new ShaderPublication(fixture.Output, "test").Publish(
            fixture.New, fixture.Manifest(fixture.New), "new", cancellationToken: cancelled.Token));
        Assert.True(fixture.Matches(fixture.Old));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
    }

    /// <summary>No-change reuse must reject output mutation observed during final input validation.</summary>
    [Fact]
    public void NoChangeOutputMutationCannotPublishSuccess()
    {
        using var fixture = new PublicationFixture();
        fixture.Publish(fixture.Old, "old");
        Assert.Throws<InvalidDataException>(() => new ShaderPublication(fixture.Output, "test").Publish(
            fixture.Old, fixture.Manifest(fixture.Old), "new", () => ShaderBuildReceipt.Publish(fixture.Output, "new"),
            () => File.WriteAllBytes(fixture.Binary("same.spv"), [0]), TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
    }

    /// <summary>A failed directory install restores the prior generation before propagating failure.</summary>
    [Fact]
    public void InstallRenameFailureRollsBackPrevious()
    {
        using var fixture = new PublicationFixture();
        fixture.Publish(fixture.Old, "old");
        var publisher = new ShaderPublication(fixture.Output, "test", checkpoint: point =>
        {
            // Occupy the destination after the old generation moved, forcing Directory.Move to fail.
            if (point == ShaderPublicationPoint.PreviousMoved) Directory.CreateDirectory(fixture.Active);
        });
        Assert.Throws<IOException>(() => fixture.Publish(fixture.New, "new", publisher));
        Assert.True(fixture.Matches(fixture.Old));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
    }

    /// <summary>Recovery retains evidence and fails closed when neither installed nor prior content is valid.</summary>
    [Fact]
    public void AmbiguousDamagedGenerationsRetainJournalWithoutSuccess()
    {
        using var fixture = new PublicationFixture();
        fixture.Publish(fixture.Old, "old");
        var publisher = new ShaderPublication(fixture.Output, "test", checkpoint: point =>
        {
            if (point == ShaderPublicationPoint.Installed) throw new IOException("stop");
        }, recoverOnFailure: false);
        Assert.Throws<IOException>(() => fixture.Publish(fixture.New, "new", publisher));
        File.WriteAllBytes(fixture.Binary("change.spv"), [0]);
        string previous = Assert.Single(Directory.GetDirectories(Path.Combine(fixture.Output, "test"), ".shader-previous-*"));
        File.WriteAllBytes(Path.Combine(previous, "change.spv"), [0]);
        Assert.Throws<InvalidDataException>(() => new ShaderPublication(fixture.Output, "test").Recover());
        Assert.True(File.Exists(Path.Combine(fixture.Output, "_tmp", "publication.json")));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
    }

    /// <summary>A manifest belonging to different bytes or membership cannot replace the prior catalogue.</summary>
    [Fact]
    public void MismatchedManifestCannotInstall()
    {
        using var fixture = new PublicationFixture();
        fixture.Publish(fixture.Old, "old");
        Assert.Throws<InvalidDataException>(() => new ShaderPublication(fixture.Output, "test").Publish(
            fixture.New, fixture.Manifest(fixture.Old), "new", cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(fixture.Matches(fixture.Old));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
    }

    /// <summary>Nested runtime binary paths retain valid manifest associations through publication.</summary>
    [Fact]
    public void NestedBinaryPathsArePublishedAndVerified()
    {
        using var fixture = new PublicationFixture();
        var binaries = new Dictionary<string, byte[]> { ["nested/fixture.spv"] = [1, 2, 3] };
        fixture.Publish(binaries, "nested");
        Assert.Equal(binaries["nested/fixture.spv"], File.ReadAllBytes(fixture.Binary("nested/fixture.spv")));
        Assert.True(ShaderBuildReceipt.IsCurrent(fixture.Output, "nested"));
    }

    /// <summary>Forced copies preserve retained timestamps and removed membership is pruned only by commit.</summary>
    [Fact]
    public void CopyFallbackPreservesUnchangedFilesAndPrunesRemovedOutputs()
    {
        using var fixture = new PublicationFixture();
        fixture.Publish(fixture.Old, "old");
        File.SetLastWriteTimeUtc(fixture.Binary("same.spv"), DateTime.UnixEpoch);
        var publisher = new ShaderPublication(fixture.Output, "test", tryLink: (_, _) => false);
        var result = fixture.Publish(fixture.New, "new", publisher);
        Assert.False(result.Unchanged);
        Assert.True(result.Copied > 0);
        Assert.Equal(0, result.Linked);
        Assert.Equal(DateTime.UnixEpoch, File.GetLastWriteTimeUtc(fixture.Binary("same.spv")));
        Assert.False(File.Exists(fixture.Binary("removed.spv")));
        Assert.True(fixture.Matches(fixture.New));
    }

    /// <summary>An identical generation keeps directory and manifest timestamps while refreshing success.</summary>
    [Fact]
    public void IdenticalGenerationSkipsReplacement()
    {
        using var fixture = new PublicationFixture();
        fixture.Publish(fixture.Old, "old");
        Directory.SetCreationTimeUtc(fixture.Active, DateTime.UnixEpoch);
        File.SetLastWriteTimeUtc(Path.Combine(fixture.Active, ShaderBinaryDigest.FileName), DateTime.UnixEpoch);
        var result = fixture.Publish(fixture.Old, "new");
        Assert.True(result.Unchanged);
        Assert.Equal(0, result.Linked + result.Copied + result.Written);
        Assert.Equal(DateTime.UnixEpoch, Directory.GetCreationTimeUtc(fixture.Active));
        Assert.Equal(DateTime.UnixEpoch, File.GetLastWriteTimeUtc(Path.Combine(fixture.Active, ShaderBinaryDigest.FileName)));
        Assert.True(ShaderBuildReceipt.IsCurrent(fixture.Output, "new"));
    }

    /// <summary>Verified desired bytes repair damaged active binaries without accepting their old receipt.</summary>
    [Fact]
    public void DamagedActiveOutputIsReplacedFromVerifiedBytes()
    {
        using var fixture = new PublicationFixture();
        fixture.Publish(fixture.Old, "old");
        File.WriteAllBytes(fixture.Binary("same.spv"), [0]);
        Assert.False(ShaderBuildReceipt.IsCurrent(fixture.Output, "old"));
        var result = fixture.Publish(fixture.Old, "new");
        Assert.False(result.Unchanged);
        Assert.True(fixture.Matches(fixture.Old));
        Assert.True(ShaderBuildReceipt.IsCurrent(fixture.Output, "new"));
    }

    /// <summary>Receipt failures never retain a success marker even after a full generation is installed.</summary>
    [Fact]
    public void ReceiptFailureInvalidatesSuccessAndRemainsRecoverable()
    {
        using var fixture = new PublicationFixture();
        fixture.Publish(fixture.Old, "old");
        Assert.Throws<IOException>(() => new ShaderPublication(fixture.Output, "test").Publish(
            fixture.New, fixture.Manifest(fixture.New), "new", () => throw new IOException("receipt unavailable"), cancellationToken: TestContext.Current.CancellationToken));
        new ShaderPublication(fixture.Output, "test").Recover();
        Assert.True(fixture.Matches(fixture.Old) || fixture.Matches(fixture.New));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
    }

    /// <summary>Rejected input snapshots and cancellation preserve a coherent generation without success.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void InputMutationAtEitherCommitCheckFailsWithoutSuccess(int failAt)
    {
        using var fixture = new PublicationFixture();
        fixture.Publish(fixture.Old, "old");
        int checks = 0;
        Assert.Throws<InvalidOperationException>(() => new ShaderPublication(fixture.Output, "test").Publish(
            fixture.New, fixture.Manifest(fixture.New), "new", () => ShaderBuildReceipt.Publish(fixture.Output, "new"),
            () => { if (++checks == failAt) throw new InvalidOperationException("Input changed"); }, TestContext.Current.CancellationToken));
        new ShaderPublication(fixture.Output, "test").Recover();
        Assert.True(fixture.Matches(fixture.Old) || fixture.Matches(fixture.New));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
    }

    /// <summary>Runtime output names cannot escape generation ownership during staging.</summary>
    [Theory]
    [InlineData("../outside.spv")]
    [InlineData("C:/outside.spv")]
    public void EscapingOutputNamesAreRejected(string name)
    {
        using var fixture = new PublicationFixture();
        var binaries = new Dictionary<string, byte[]> { [name] = [1] };
        Assert.ThrowsAny<Exception>(() => fixture.Publish(binaries, "bad"));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
    }
    #endregion

    #region Private
    /// <summary>Owns a tiny binary catalogue and exact manifest/content assertions.</summary>
    private sealed class PublicationFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "vge-publication-" + Guid.NewGuid().ToString("N"));
        public string Output => Path.Combine(Root, "output");
        public string Active => Path.Combine(Output, "test", "shaders");
        public Dictionary<string, byte[]> Old { get; } = new() { ["same.spv"] = [1, 2], ["change.spv"] = [3], ["removed.spv"] = [4] };
        public Dictionary<string, byte[]> New { get; } = new() { ["same.spv"] = [1, 2], ["change.spv"] = [5], ["added.spv"] = [6] };

        #region Public API
        /// <summary>Creates the isolated output root.</summary>
        public PublicationFixture() => Directory.CreateDirectory(Output);
        /// <summary>Publishes a complete generation with its success receipt.</summary>
        public ShaderPublicationResult Publish(IReadOnlyDictionary<string, byte[]> binaries, string fingerprint, ShaderPublication? publisher = null)
            => (publisher ?? new ShaderPublication(Output, "test")).Publish(binaries, Manifest(binaries), fingerprint,
                () => ShaderBuildReceipt.Publish(Output, fingerprint));
        /// <summary>Encodes authoritative binary digests for a generation.</summary>
        public byte[] Manifest(IReadOnlyDictionary<string, byte[]> binaries)
            => ShaderBinaryDigest.Encode(binaries.ToDictionary(pair => pair.Key, pair => ShaderVariantCache.Digest(pair.Value)));
        /// <summary>Names one active runtime binary.</summary>
        public string Binary(string name) => Path.Combine(Active, name);
        /// <summary>Checks exact file membership and all binary and manifest bytes.</summary>
        public bool Matches(IReadOnlyDictionary<string, byte[]> binaries)
        {
            if (!Directory.Exists(Active)) return false;
            string[] expected = binaries.Keys.Append(ShaderBinaryDigest.FileName).Order(StringComparer.Ordinal).ToArray();
            string[] actual = Directory.GetFiles(Active).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;
            return expected.SequenceEqual(actual) && binaries.All(pair => File.ReadAllBytes(Binary(pair.Key)).SequenceEqual(pair.Value))
                && File.ReadAllBytes(Path.Combine(Active, ShaderBinaryDigest.FileName)).SequenceEqual(Manifest(binaries));
        }
        /// <summary>Removes only this fixture's private temporary tree.</summary>
        public void Dispose() => Directory.Delete(Root, recursive: true);
        #endregion
    }
    #endregion
}
