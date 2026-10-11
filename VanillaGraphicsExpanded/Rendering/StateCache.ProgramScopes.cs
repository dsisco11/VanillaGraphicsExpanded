using System;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Extends the existing program tracker with scoped owner, generation and context metadata.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Creates a binding-only scope without inferring an owner from a numeric program name.</summary>
    public ProgramScope UseProgramScope(int programId)
    {
        var scope = ProgramScopeTracker.Capture(false);
        try { UseProgram(programId); return new ProgramScope(scope); }
        catch { scope.Dispose(); throw; }
    }

    /// <summary>Shares one exactly-once token between copies of an existing program scope.</summary>
    public readonly struct ProgramScope : IDisposable
    {
        private readonly IDisposable? scope;
        /// <summary>Retains a tracker-owned restoration token.</summary>
        internal ProgramScope(IDisposable scope) => this.scope = scope;
        /// <summary>Restores only while this token owns the top scoped lifetime.</summary>
        public void Dispose() => scope?.Dispose();
        /// <summary>Reports consumption so native borrowers can distinguish failed cleanup from rejected ordering.</summary>
        internal bool IsEnded => scope is ProgramScopeTracker.Frame frame && frame.IsEnded;
    }
    #endregion

    #region Internal API
    /// <summary>Returns an admitted owner only when its binding and native context still match.</summary>
    internal static IGpuProgram? ActiveProgram => ProgramScopeTracker.CurrentOwner();
    /// <summary>Captures managed restoration in the existing tracker.</summary>
    internal IDisposable CaptureProgramScope(bool replayInputs = true) => ProgramScopeTracker.Capture(replayInputs);
    /// <summary>Establishes explicit use lifetime before binding and publishing inputs.</summary>
    internal void ActivateProgram(IGpuProgram owner) => ProgramScopeTracker.Activate(owner);
    /// <summary>Stops only the matching managed activation, leaving unrelated bindings intact.</summary>
    internal void StopProgram(IGpuProgram owner) => ProgramScopeTracker.Stop(owner);
    #endregion

    #region Private
    /// <summary>Owns the single program-scope chain and direct-use metadata, independent of engine shader state.</summary>
    private static class ProgramScopeTracker
    {
        [ThreadStatic] private static Binding? current;
        [ThreadStatic] private static Frame? top;

        #region Internal API
        #region Activation and capture
        /// <summary>Withdraws direct owner admission when raw code explicitly selects a binding.</summary>
        internal static void ForgetCurrent()
        {
            var value = current; current = null;
            value?.Release();
        }
        /// <summary>Validates the scoped owner against context and native binding knowledge.</summary>
        internal static IGpuProgram? CurrentOwner()
        {
            SynchronizeContext();
            if (current == null) return null;
            if (current.Context != RenderContextRegistry.Current() ||
                (Current.TryGetCachedCurrentProgram(out int id) && id != current.ProgramId))
            { ForgetCurrent(); return null; }
            current.Validate();
            return current.Owner;
        }
        /// <summary>Captures restoration metadata and borrows the suspended generation.</summary>
        internal static Frame Capture(bool replay)
        {
            NativeShaderHandoff.RequireInactive();
            // Resolve unknown native binding through the existing cache before admitting restoration ownership.
            int raw = Current.GetCurrentProgram();
            _ = CurrentOwner();
            var previous = current;
            previous?.Retain();
            var frame = new Frame(top, previous, raw, RenderContextRegistry.Current(), replay);
            top = frame;
            return frame;
        }
        /// <summary>Admits an installed generation without creating an additional owner stack.</summary>
        internal static void Activate(IGpuProgram owner)
        {
            NativeShaderHandoff.RequireInactive();
            SynchronizeContext();
            var context = RenderContextRegistry.Current();
            if (context.Handle == 0 || owner.ExecutableContext != context)
                throw new InvalidOperationException("GPU executable belongs to a different or retired render context.");
            if (current?.Owner == owner && current.Revision == owner.ExecutableRevision && ReferenceEquals(current.Scope, top))
            { current.Validate(); Current.UseProgramBinding(owner.ProgramId); return; }
            ForgetCurrent();
            current = new Binding(owner, top, context);
            top?.Admit(current);
            Current.UseProgramBinding(owner.ProgramId);
        }
        #endregion
        #region Cleanup and retirement
        /// <summary>Releases managed sampler ownership only for the current matching generation.</summary>
        internal static void Stop(IGpuProgram owner)
        {
            SynchronizeContext();
            if (current?.Owner != owner) return;
            current.Validate();
            bool bound = Current.GetCurrentProgram() == current.ProgramId;
            ForgetCurrent();
            if (bound)
            {
                owner.ProgramLayout.ReleaseSamplerBindings();
                Current.UseProgramBinding(0);
            }
        }
        /// <summary>Marks names retired in the existing scope chain, without an auxiliary handle registry.</summary>
        internal static void MarkDeleted(int program)
        {
            if (program == 0) return;
            if (current?.ProgramId == program) current.Retired = true;
            for (var frame = top; frame != null; frame = frame.Parent) frame.MarkDeleted(program);
        }
        #endregion
        #endregion

        #region Private
        /// <summary>Abandons obsolete metadata without issuing native calls into a replacement context.</summary>
        private static void SynchronizeContext()
        {
            var context = RenderContextRegistry.Current();
            if (current != null && current.Context != context) ForgetCurrent();
            while (top != null && top.Context != context)
            {
                var obsolete = top;
                top = obsolete.Parent;
                obsolete.Abandon();
            }
        }
        /// <summary>Borrows one installed generation; a captured previous binding adds a second borrow.</summary>
        internal sealed class Binding
        {
            internal bool Retired;
            internal readonly IGpuProgram Owner;
            internal Frame? Scope;
            internal readonly int ProgramId;
            internal readonly ulong Revision;
            internal readonly (nint Handle, long Generation) Context;
            /// <summary>Retains the admitted owner's immutable executable identity.</summary>
            internal Binding(IGpuProgram owner, Frame? scope, (nint Handle, long Generation) context)
            { Owner = owner; Scope = scope; Context = context; ProgramId = owner.ProgramId; Revision = owner.ExecutableRevision; Retain(); }
            /// <summary>Retains suspended native ownership without copying a handle owner.</summary>
            internal void Retain() => Owner.Lifetime.RetainBorrow();
            /// <summary>Releases only this metadata's lifetime borrow.</summary>
            internal void Release() => Owner.Lifetime.ReleaseBorrow();
            /// <summary>Rejects stale identity before any native restoration call.</summary>
            internal void Validate()
            {
                if (Retired || Context != RenderContextRegistry.Current() || Owner.ExecutableContext != Context ||
                    Owner.Lifetime.IsRetired || Owner.ProgramId != ProgramId || Owner.ExecutableRevision != Revision)
                    throw new InvalidOperationException("GPU scope has a stale executable or render context.");
            }
        }
        /// <summary>Restores the captured binding once, in stack order and on its originating context.</summary>
        internal sealed class Frame : IDisposable
        {
            private readonly Frame? parent;
            private readonly Binding? previous;
            private readonly int raw;
            private readonly (nint Handle, long Generation) context;
            private readonly bool replay;
            private Binding? entered;
            private bool abandoned;
            private bool rawRetired;
            private bool ended;

            #region Public API
            /// <summary>Rejects stale/out-of-order tokens before they can unbind a later activation.</summary>
            public void Dispose()
            {
                if (abandoned) throw new InvalidOperationException("Program scope context was retired.");
                if (ended) return;
                if (context != RenderContextRegistry.Current())
                    throw new InvalidOperationException("Program scope restoration requires its originating render context.");
                if (!ReferenceEquals(top, this)) throw new InvalidOperationException("Program scopes must end in reverse order.");
                try
                {
                    previous?.Validate();
                    entered?.Validate();
                    if (previous == null && rawRetired) throw new InvalidOperationException("Captured raw program was retired.");
                }
                catch (Exception failure)
                {
                    // Stale restoration has no authority over the later binding.
                    ended = true; top = parent;
                    if (current != null && ReferenceEquals(current.Scope, this))
                    { current.Scope = parent; parent?.Admit(current); }
                    previous?.Release(); entered?.Release(); entered = null;
                    throw new Shaders.ShaderOwnershipRestoreException(failure);
                }
                ended = true;
                try
                {
                    // Only this scope may clear its entered owner. Raw changes carry no managed owner.
                    if (current != null && ReferenceEquals(current.Scope, this)) Stop(current.Owner);
                    else ForgetCurrent();
                    top = parent;
                    if (previous != null)
                    {
                        Activate(previous.Owner);
                        if (replay) previous.Owner.ReplayInputs();
                    }
                    else Current.UseProgramBinding(raw);
                }
                catch (Exception failure)
                {
                    ForgetCurrent();
                    try { Current.UseProgramBinding(0); }
                    catch (Exception cleanup) { throw new Shaders.ShaderOwnershipRestoreException(new AggregateException(failure, cleanup)); }
                    Current.Invalidate(EPipelineState.Program);
                    throw new Shaders.ShaderOwnershipRestoreException(failure);
                }
                finally { top = parent; previous?.Release(); entered?.Release(); entered = null; }
            }
            #endregion

            #region Internal API
            /// <summary>Exposes this token context for metadata withdrawal.</summary>
            internal (nint Handle, long Generation) Context => context;
            /// <summary>Exposes the existing linked scope chain.</summary>
            internal Frame? Parent => parent;
            /// <summary>Borrows the entered generation even when raw work suspends its binding.</summary>
            internal void Admit(Binding value)
            {
                if (ReferenceEquals(entered, value)) return;
                entered?.Release(); entered = value; entered.Retain();
            }
            /// <summary>Marks captured native names retired before they can be numerically reused.</summary>
            internal void MarkDeleted(int program)
            {
                if (previous?.ProgramId == program) previous.Retired = true;
                if (entered?.ProgramId == program) entered.Retired = true;
                if (raw == program && program != 0) rawRetired = true;
            }
            /// <summary>Consumes retired-context metadata without touching native state.</summary>
            internal void Abandon()
            {
                if (ended) return;
                abandoned = ended = true;
                previous?.Release(); entered?.Release(); entered = null;
            }
            /// <summary>Reports consumed metadata without touching native state.</summary>
            internal bool IsEnded => ended;
            /// <summary>Captures the previous scope and its borrowed owner or raw binding.</summary>
            internal Frame(Frame? parent, Binding? previous, int raw, (nint Handle, long Generation) context, bool replay)
            { this.parent = parent; this.previous = previous; this.raw = raw; this.context = context; this.replay = replay; }
            #endregion
        }
        #endregion
    }
    #endregion
}
