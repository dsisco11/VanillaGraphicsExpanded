using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns deferred program linking without looking up completion by a reusable numeric name.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Submits a link and explicitly supersedes any earlier pending completion.</summary>
    internal unsafe void SubmitProgramExecutable(GpuProgramObject program, RHIShaderBinary? binary = null)
    {
        RequireContext();
        ObjectDisposedException.ThrowIf(!program.IsValid, program);
        GpuResourceManager.RequireWrapperMetadataAccess(program);
        cache.RequireResourceOperationAvailable(GpuResourceKind.Program, program.ProgramId);
        if (program.ResourceManager?.GetResourceRegistration(program) is GpuResourceMetadataEntry<GpuProgramMetadata> entry
            && (entry.OperationInProgress || entry.CompletionInProgress))
            throw new InvalidOperationException("A program operation is already in progress.");
        var pending = new PendingProgramLink(program);
        program.PendingLink = pending;
        var guard = cache.EnterResourceOperation(new(GpuResourceKind.Program, program.ProgramId));
        var tracked = pending.Entry as GpuResourceMetadataEntry<GpuProgramMetadata>;
        if (tracked is not null) tracked.OperationInProgress = true;
        try
        {
            GpuResourceManager.InvalidateWrapperMetadata(program, GpuMetadataChange.Metadata | GpuMetadataChange.Executable);
            if (pending.Entry is null)
            {
                GpuResourceManager.DiscardUntrackedMetadata(program);
                program.OnNativeProgramMetadataChanged(GpuMetadataChange.Executable, false);
            }
            if (binary is null) GL.LinkProgram(program.ProgramId);
            else
            {
                fixed (byte* bytes = binary.Bytes.AsSpan())
                    GL.ProgramBinary(program.ProgramId, (BinaryFormat)binary.NativeFormat, (nint)bytes, binary.Bytes.Length);
            }
            RequireContext();
        }
        catch { if (ReferenceEquals(program.PendingLink, pending)) program.PendingLink = null; throw; }
        finally
        {
            if (tracked is not null) tracked.OperationInProgress = false;
            cache.ExitResourceOperation(guard);
        }
    }

    /// <summary>Consumes the original pending result once and publishes driver-produced executable facts.</summary>
    internal bool CompleteProgramLink(GpuProgramObject program, out string infoLog, IReadOnlyList<ShaderStage>? stages = null)
    {
        RequireContext();
        var pending = program.PendingLink;
        infoLog = string.Empty;
        if (pending is null || pending.Consumed || !pending.IsCurrent(program))
        {
            infoLog = "The pending program allocation or originating context is no longer current.";
            return false;
        }
        pending.Consumed = true;
        // Identity is checked before any driver access; the metadata reader and publication
        // also retain the original association if callbacks retire the object during completion.
        try
        {
            const GpuMetadataChange change = GpuMetadataChange.Metadata | GpuMetadataChange.Executable;
            using var operation = new GpuResourceManager.MetadataOperation<GpuProgramMetadata>(program, change);
            var reader = (pending.Entry as GpuResourceMetadataEntry<GpuProgramMetadata>)?.Reader ?? GpuProgramMetadataReader.Instance;
            if (!reader.TryRead(program.ProgramId, out var candidate))
                throw new InvalidOperationException("Program metadata is unavailable after link completion.");
            if (candidate.ExecutableAvailable && stages is not null)
                candidate = ReadExecutableStageMetadata(program, candidate, stages);
            if (!candidate.ExecutableAvailable) infoLog = GL.GetProgramInfoLog(program.ProgramId) ?? string.Empty;
            if (!pending.IsCurrent(program)) return false;
            operation.Commit(candidate);
            if (pending.Entry is null) program.OnNativeProgramMetadataChanged(change, true);
            return candidate.ExecutableAvailable;
        }
        finally { if (ReferenceEquals(program.PendingLink, pending)) program.PendingLink = null; }
    }
    #endregion

    /// <summary>Captures one deferred link's identity and context without owning another resource registration.</summary>
    internal sealed class PendingProgramLink
    {
        private readonly int id;
        private readonly (nint Handle, long Generation) context;
        internal readonly GpuResourceRegistration? Entry;
        private readonly ulong revision;
        internal bool Consumed;

        #region Internal API
        /// <summary>Captures the original owner before native submission.</summary>
        internal PendingProgramLink(GpuProgramObject program)
        {
            id = program.ProgramId;
            context = RenderContextRegistry.Current();
            Entry = program.ResourceManager?.GetResourceRegistration(program);
            revision = Entry?.MetadataRevision ?? 0;
        }
        /// <summary>Rejects retired names, context changes and substituted registrations.</summary>
        internal bool IsCurrent(GpuProgramObject program) => ReferenceEquals(program.PendingLink, this)
            && program.IsValid && program.ProgramId == id
            && context == RenderContextRegistry.Current()
            && (context == default || RenderContextRegistry.IsAlive(context))
            && (Entry is null ? program.ResourceManager?.GetResourceRegistration(program) is null
                : Entry.Manager.IsRegistered(Entry) && Entry.State == GpuResourceRegistrationState.Live && Entry.MetadataRevision == revision
                    && ReferenceEquals(program.ResourceManager?.GetResourceRegistration(program), Entry));
        #endregion
    }
}
