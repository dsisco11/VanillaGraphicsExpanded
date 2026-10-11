using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Owns declarations and asset readiness for one explicitly composed rendering lifetime.</summary>
internal sealed class GpuShaderPrograms(Func<string, ShaderAssetSource> assets) : IDisposable
{
    private readonly Dictionary<string, (GpuProgramDeclaration Declaration, GpuProgram Owner)> programs = new(StringComparer.Ordinal);
    private bool closed;
    private bool submissionsClosed;
    private bool reloading;
    internal long Epoch { get; private set; }
    internal bool IsClosed => closed;
    internal bool IsReloading => reloading;

    #region Public API
    #region Declarations
    /// <summary>Returns an identical declaration without invoking its factory; incompatible duplicates fail before mutation.</summary>
    internal T Declare<T>(GpuProgramDeclaration declaration, Func<T> factory) where T : GpuProgram
    {
        RequireOpen();
        ObjectDisposedException.ThrowIf(submissionsClosed, this);
        if (programs.TryGetValue(declaration.Name, out var existing))
        {
            if (!existing.Declaration.Matches(declaration))
                throw new InvalidOperationException("Incompatible VGE declaration: " + declaration.Name);
            if (existing.Owner.IsRetired) throw new InvalidOperationException("Remove the retired VGE declaration before redeclaring: " + declaration.Name);
            return (T)existing.Owner;
        }
        var owner = factory();
        try
        {
            if (!declaration.Matches(new(declaration.Domain, declaration.Name, owner.GetType(), owner.RequestedSettings)))
                throw new InvalidOperationException("VGE factory does not match declaration: " + declaration.Name);
            owner.PassName = declaration.Name;
            owner.Initialize(assets(declaration.Domain));
            owner.PreparationAdmission = () => !closed && !submissionsClosed && !reloading;
            programs.Add(declaration.Name, (declaration, owner));
            return owner;
        }
        catch { owner.Dispose(); throw; }
    }

    /// <summary>Returns a typed owner; absent entries remain optional and incompatible types produce a diagnostic.</summary>
    internal T? Get<T>(string name) where T : GpuProgram
    {
        RequireOpen();
        if (!programs.TryGetValue(name, out var entry))
        {
            assets(PBR.ShaderImportsSystem.DefaultDomain).Warning?.Invoke("Missing VGE shader declaration: " + name);
            return null;
        }
        return entry.Owner as T ?? throw new InvalidOperationException($"VGE declaration '{name}' is {entry.Owner.GetType().Name}, requested {typeof(T).Name}.");
    }

    /// <summary>Captures declarations without preparation or native work.</summary>
    internal ImmutableArray<GpuProgram> GetAll() { RequireOpen(); return programs.Values.Select(e => e.Owner).ToImmutableArray(); }

    /// <summary>Retires an explicitly removed declaration without permitting implicit revival.</summary>
    internal void Remove(string name)
    {
        RequireOpen();
        if (!programs.TryGetValue(name, out var entry)) return;
        entry.Owner.Dispose();
        programs.Remove(name);
    }
    #endregion

    #region Preparation and reload
    /// <summary>Checks the entire selection before submitting a bounded compilation window.</summary>
    internal bool Preload(ImmutableArray<GpuProgram> selection)
    {
        RequireOpen();
        ObjectDisposedException.ThrowIf(submissionsClosed, this);
        if (reloading) throw new InvalidOperationException("VGE shader reload is in progress.");
        foreach (var owner in selection)
        {
            if (!programs.Values.Any(e => ReferenceEquals(e.Owner, owner))) throw new InvalidOperationException("Preload requires a declaration from this registry.");
            owner.RequireOutsideSubmission();
            if (owner.RequiresPreparation) ((IGpuProgram)owner).Lifetime.RequireUnborrowed();
        }
        bool success = true;
        foreach (var group in selection.Distinct().Where(o => !o.IsRetired && o.RequiresPreparation && o.CanSubmitPreparation).GroupBy(o => o.AssetDomain))
        {
            using var batch = new ShaderLinkBatch(assets(group.Key), group.Select(o => o.RequestedSettings));
            foreach (var owner in group) success &= owner.EnsureReady();
        }
        return success && selection.All(o => !o.RequiresPreparation);
    }

    /// <summary>Validates all borrowers before withdrawing any readiness or entering the native reload.</summary>
    internal void BeginReload()
    {
        RequireOpen();
        ObjectDisposedException.ThrowIf(submissionsClosed, this);
        if (reloading) throw new InvalidOperationException("Recursive VGE shader reload.");
        foreach (var entry in programs.Values)
        {
            entry.Owner.RequireOutsideSubmission();
            ((IGpuProgram)entry.Owner).Lifetime.RequireUnborrowed();
        }
        foreach (var entry in programs.Values) if (!entry.Owner.IsRetired) entry.Owner.InvalidateAssets();
        reloading = true;
        Epoch++;
    }

    /// <summary>Reopens demand preparation only after native refresh has returned or failed.</summary>
    internal void CompleteReload() { if (!closed) reloading = false; }

    /// <summary>Invalidates queued world work without changing the native context or declarations.</summary>
    internal void LeaveWorld() { RequireOpen(); Epoch++; }
    #endregion

    #region Lifetime
    /// <summary>Stops executable use before borrower cleanup while retaining explicit removal for retiring features.</summary>
    internal void CloseSubmissionAdmission() => submissionsClosed = true;
    /// <summary>Rejects terminal teardown before changing admission when an owner is publishing or borrowed.</summary>
    internal void RequireRetirementBoundary()
    {
        foreach (var entry in programs.Values)
        {
            ((IGpuProgram)entry.Owner).Lifetime.RequireOutsidePublication();
            ((IGpuProgram)entry.Owner).Lifetime.RequireUnborrowed();
        }
    }
    /// <summary>Closes admission permanently and attempts every independent owner cleanup.</summary>
    public void Dispose()
    {
        if (closed) return;
        RequireRetirementBoundary();
        closed = true;
        Epoch++;
        List<Exception> failures = [];
        foreach (var entry in programs.Values)
            try { entry.Owner.Dispose(); } catch (Exception error) { failures.Add(error); }
        programs.Clear();
        if (failures.Count != 0) throw new AggregateException(failures);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Rejects late callbacks instead of recreating a retired library.</summary>
    private void RequireOpen() => ObjectDisposedException.ThrowIf(closed, this);
    #endregion
}

