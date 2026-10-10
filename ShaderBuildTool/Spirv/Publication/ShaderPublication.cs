using System.Diagnostics;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Spirv;

/// <summary>Identifies durable publication boundaries for cancellation and interruption diagnostics.</summary>
internal enum ShaderPublicationPoint { Staging, Staged, Prepared, PreviousMoved, Installed, BeforeReceipt, ReceiptPublished, Cleanup }

/// <summary>Reports whether publication reused the generation and how binary staging was performed.</summary>
internal sealed record ShaderPublicationResult(bool Unchanged, int Linked, int Copied, int Written);

/// <summary>Publishes coherent shader generations under the caller's output lease and recovers interrupted writers.</summary>
internal sealed class ShaderPublication
{
    private readonly ShaderPublicationPaths paths;
    private readonly Action<ShaderPublicationPoint>? checkpoint;
    private readonly Func<string, string, bool> tryLink;
    private readonly bool recoverOnFailure;
    private readonly Action<string>? report;

    #region Public API
    /// <summary>Configures publication; disabling failure recovery permits diagnostic simulation of abrupt termination.</summary>
    internal ShaderPublication(string outputRoot, string domain, Action<ShaderPublicationPoint>? checkpoint = null,
        Func<string, string, bool>? tryLink = null, bool recoverOnFailure = true, Action<string>? report = null)
    {
        paths = new(outputRoot, domain);
        this.checkpoint = checkpoint;
        this.tryLink = tryLink ?? ShaderBinaryReuse.TryLink;
        this.recoverOnFailure = recoverOnFailure;
        this.report = report;
    }

    /// <summary>Reconciles durable state before receipt evaluation or explicit clean, without trusting journal paths.</summary>
    internal void Recover()
    {
        if (!File.Exists(paths.Journal)) return;
        try
        {
            var journal = ShaderPublicationJournal.Read(paths);
            bool previousExists = Directory.Exists(journal.Previous);
            bool activeExists = Directory.Exists(paths.Active);
            // Before active mutation, only staging can be discarded. No success marker survives a rebuild.
            if (journal.State == ShaderPublicationState.Staging && !previousExists || !previousExists && (journal.Prior is null && !activeExists
                || journal.Prior is not null && journal.Prior.Matches(paths.Active)))
            {
                File.Delete(paths.Receipt);
                Cleanup(journal);
                report?.Invoke("Recovered shader publication before installation.");
                return;
            }
            if (journal.Next is not null && journal.Next.Matches(paths.Active) && ShaderGenerationSnapshot.IsCoherent(paths.Active))
            {
                // A crash can occur after receipt replacement but before its journal revision.
                bool committed = journal.Fingerprint.Length != 0 && ShaderBuildReceipt.IsCurrent(paths.Output, journal.Fingerprint);
                if (!committed) File.Delete(paths.Receipt);
                Cleanup(journal);
                report?.Invoke(committed ? "Recovered committed shader publication." : "Retained verified generation without a success receipt.");
                return;
            }
            File.Delete(paths.Receipt);
            if (journal.PriorCoherent && journal.Prior!.Matches(journal.Previous) && ShaderGenerationSnapshot.IsCoherent(journal.Previous))
            {
                DeleteActive();
                Directory.Move(journal.Previous, paths.Active);
                Cleanup(journal);
                report?.Invoke("Restored previous complete shader generation.");
                return;
            }
            // Repair builds may have started with damaged outputs. A verified pending generation is then the only complete source.
            if (!activeExists && journal.Next is not null && journal.Next.Matches(journal.Pending) && ShaderGenerationSnapshot.IsCoherent(journal.Pending))
            {
                Directory.Move(journal.Pending, paths.Active);
                Cleanup(journal);
                report?.Invoke("Recovered verified pending shader generation without a success receipt.");
                return;
            }
            throw new InvalidDataException("Ambiguous or damaged shader publication; inspect and repair the output tree before rebuilding.");
        }
        catch
        {
            File.Delete(paths.Receipt);
            throw;
        }
    }

    /// <summary>Stages complete outputs, installs them, and commits the supplied success receipt last.</summary>
    internal ShaderPublicationResult Publish(IReadOnlyDictionary<string, byte[]> binaries, byte[] manifest, string fingerprint = "",
        Action? publishReceipt = null, Action? validateInputs = null, CancellationToken cancellationToken = default)
    {
        Recover();
        File.Delete(paths.Receipt);
        var timer = Stopwatch.StartNew();
        ShaderPublicationJournal? journal = null;
        try
        {
            var expected = Expected(binaries, manifest);
            if (expected.Matches(paths.Active) && ShaderGenerationSnapshot.IsCoherent(paths.Active))
            {
                validateInputs?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
                CommitReceipt(publishReceipt, validateInputs, () =>
                {
                    if (!expected.Matches(paths.Active) || !ShaderGenerationSnapshot.IsCoherent(paths.Active))
                        throw new InvalidDataException("Published generation changed before receipt commit.");
                }, cancellationToken);
                report?.Invoke($"Publication unchanged; linked=0; copied=0; written=0; elapsedMs={timer.Elapsed.TotalMilliseconds:F1}.");
                return new(true, 0, 0, 0);
            }
            string id = Guid.NewGuid().ToString("N");
            journal = new(1, id, paths.Active, paths.Transaction(id, false), paths.Transaction(id, true), fingerprint, ShaderPublicationState.Staging,
                Directory.Exists(paths.Active) ? ShaderGenerationSnapshot.Capture(paths.Active) : null,
                Directory.Exists(paths.Active) && ShaderGenerationSnapshot.IsCoherent(paths.Active), null);
            journal.Save(paths);
            Directory.CreateDirectory(journal.Pending);
            Step(ShaderPublicationPoint.Staging, cancellationToken);
            int linked = 0, copied = 0, written = 0;
            long copyTicks = 0;
            foreach (var pair in binaries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = pair.Key.Replace('/', Path.DirectorySeparatorChar);
                string destination = ShaderPublicationPaths.FileWithin(journal.Pending, relative);
                string prior = ShaderPublicationPaths.FileWithin(paths.Active, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (File.Exists(prior) && File.ReadAllBytes(prior).AsSpan().SequenceEqual(pair.Value))
                {
                    // Neither linked file is opened for modification; every writer installs replacements.
                    bool reused;
                    try { reused = tryLink(destination, prior); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { reused = false; }
                    if (reused) linked++;
                    else
                    {
                        long copyStart = Stopwatch.GetTimestamp();
                        File.Copy(prior, destination, overwrite: false);
                        File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(prior));
                        copyTicks += Stopwatch.GetTimestamp() - copyStart;
                        copied++;
                    }
                }
                else { File.WriteAllBytes(destination, pair.Value); written++; }
            }
            string pendingManifest = Path.Combine(journal.Pending, ShaderBinaryDigest.FileName);
            File.WriteAllBytes(pendingManifest, manifest);
            string priorManifest = Path.Combine(paths.Active, ShaderBinaryDigest.FileName);
            if (File.Exists(priorManifest) && File.ReadAllBytes(priorManifest).AsSpan().SequenceEqual(manifest))
                File.SetLastWriteTimeUtc(pendingManifest, File.GetLastWriteTimeUtc(priorManifest));
            if (!expected.Matches(journal.Pending) || !ShaderGenerationSnapshot.IsCoherent(journal.Pending))
                throw new InvalidDataException("Staged shader generation failed validation.");
            Step(ShaderPublicationPoint.Staged, cancellationToken);
            journal = journal with { State = ShaderPublicationState.Prepared, Next = expected };
            journal.Save(paths);
            Step(ShaderPublicationPoint.Prepared, cancellationToken);
            validateInputs?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (journal.Prior is not null) Directory.Move(paths.Active, journal.Previous);
            Step(ShaderPublicationPoint.PreviousMoved, cancellationToken);
            Directory.Move(journal.Pending, paths.Active);
            // The checkpoint deliberately precedes the journal revision to cover rename/update interruption gaps.
            Step(ShaderPublicationPoint.Installed, cancellationToken);
            journal = journal with { State = ShaderPublicationState.Installed };
            journal.Save(paths);
            if (!expected.Matches(paths.Active) || !ShaderGenerationSnapshot.IsCoherent(paths.Active))
                throw new InvalidDataException("Installed shader generation failed validation.");
            CommitReceipt(publishReceipt, validateInputs, () =>
                {
                    if (!expected.Matches(paths.Active) || !ShaderGenerationSnapshot.IsCoherent(paths.Active))
                        throw new InvalidDataException("Published generation changed before receipt commit.");
                }, cancellationToken);
            journal = journal with { State = ShaderPublicationState.Committed };
            journal.Save(paths);
            Step(ShaderPublicationPoint.Cleanup, cancellationToken);
            Cleanup(journal);
            report?.Invoke($"Publication replaced; linked={linked}; copied={copied}; written={written}; copyWorkMs={copyTicks * 1000.0 / Stopwatch.Frequency:F1}; elapsedMs={timer.Elapsed.TotalMilliseconds:F1}.");
            return new(false, linked, copied, written);
        }
        catch when (recoverOnFailure)
        {
            File.Delete(paths.Receipt);
            // Ordinary failures roll back a verified prior generation; crash recovery uses the durable journal independently.
            if (journal is not null && journal.PriorCoherent && journal.Prior!.Matches(journal.Previous)
                && ShaderGenerationSnapshot.IsCoherent(journal.Previous))
            {
                DeleteActive();
                Directory.Move(journal.Previous, paths.Active);
                Cleanup(journal);
            }
            else Recover();
            throw;
        }
    }
    #endregion

    #region Private
    /// <summary>Constructs complete expected byte identities and rejects colliding or escaping output names.</summary>
    private ShaderGenerationSnapshot Expected(IReadOnlyDictionary<string, byte[]> binaries, byte[] manifest)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in binaries)
        {
            string path = ShaderPublicationPaths.FileWithin(paths.Active, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            if (!files.TryAdd(Path.GetRelativePath(paths.Active, path), ShaderRecordStore.Digest(pair.Value)))
                throw new InvalidDataException("Duplicate shader publication output.");
        }
        if (!files.TryAdd(ShaderBinaryDigest.FileName, ShaderRecordStore.Digest(manifest)))
            throw new InvalidDataException("Shader binary conflicts with its manifest.");
        return new(files);
    }

    /// <summary>Rechecks inputs immediately before receipt replacement and exposes both sides of the commit boundary.</summary>
    private void CommitReceipt(Action? publishReceipt, Action? validateInputs, Action verifyOutputs, CancellationToken cancellationToken)
    {
        Step(ShaderPublicationPoint.BeforeReceipt, cancellationToken);
        validateInputs?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        verifyOutputs();
        publishReceipt?.Invoke();
        verifyOutputs();
        Step(ShaderPublicationPoint.ReceiptPublished, cancellationToken);
    }

    /// <summary>Observes cancellation at each transition and allows deterministic diagnostic interruption.</summary>
    private void Step(ShaderPublicationPoint point, CancellationToken token)
    {
        checkpoint?.Invoke(point);
        token.ThrowIfCancellationRequested();
    }

    /// <summary>Checks the complete active subtree before removing a failed installed generation.</summary>
    private void DeleteActive()
    {
        ShaderPublicationPaths.EnsureUnredirected(paths.Active);
        if (!Directory.Exists(paths.Active)) return;
        ShaderGenerationSnapshot.Files(paths.Active).ToArray();
        Directory.Delete(paths.Active, recursive: true);
    }

    /// <summary>Deletes only validated transaction directories, removing recovery authority last.</summary>
    private void Cleanup(ShaderPublicationJournal journal)
    {
        journal.Validate(paths);
        paths.DeleteTransaction(journal.Id, false);
        paths.DeleteTransaction(journal.Id, true);
        File.Delete(paths.Journal);
    }
    #endregion
}
