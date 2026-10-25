using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks established executable edits publish only their affected facts without full reflection.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuProgramNarrowPublicationTests(HeadlessGLFixture fixture)
{
    private static int interfaceQueries, unitQueries, assignmentCalls;
    private static Action? duringLink;

    #region Public API
    /// <summary>Out-of-range managed binding inputs preserve canonical facts and are rejected before any native setter.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidControlledAssignmentsPreserveMetadata(int family)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.InvalidProgramAssignments." + Guid.NewGuid());
        try
        {
            using var owner = CreateProgram();
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner));
            var previous = entry.Metadata;
            ulong revision = entry.MetadataRevision;
            int location = GL.GetUniformLocation(owner.ProgramId, "inputs[0]");
            int index = family == 0 ? 0 : GL.GetProgramResourceIndex(owner.ProgramId,
                family == 1 ? ProgramInterface.UniformBlock : ProgramInterface.ShaderStorageBlock,
                family == 1 ? "Parameters" : "Output");
            var prefix = new HarmonyMethod(typeof(GpuProgramNarrowPublicationTests), nameof(CountAssignment));
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.ProgramUniform1), [typeof(int), typeof(int), typeof(int)]), prefix: prefix);
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.UniformBlockBinding), [typeof(int), typeof(int), typeof(int)]), prefix: prefix);
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.ShaderStorageBlockBinding), [typeof(int), typeof(int), typeof(int)]), prefix: prefix);
            assignmentCalls = 0;
            int limit = family switch
            {
                0 => GpuSupport.Graphics.MaxCombinedTextureImageUnits,
                1 => GpuSupport.Graphics.MaxUniformBufferBindings,
                _ => GpuSupport.Graphics.MaxShaderStorageBufferBindings
            };
            foreach (int value in new[] { -1, limit })
            {
                if (family == 0) Assert.Throws<ArgumentOutOfRangeException>(() => StateCache.Current.SetProgramUniformUnit(owner.ProgramId, location, value));
                else if (family == 1) Assert.Throws<ArgumentOutOfRangeException>(() => StateCache.Current.UniformBlockBinding(owner.ProgramId, index, value));
                else Assert.Throws<ArgumentOutOfRangeException>(() => StateCache.Current.ShaderStorageBlockBinding(owner.ProgramId, index, value));
            }
            Assert.Equal(0, assignmentCalls);
            Assert.Equal(previous, entry.Metadata);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.True(entry.MetadataAvailable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Context-local compute preparation reflects once at linking and reuses those facts through final ownership transfer.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalComputeTransferDoesNotRepeatReflection(bool contract)
    {
        fixture.EnsureContextValid();
        Assert.Null(GpuResourceManagerSystem.CurrentManager);
        var harmony = new Harmony("Tests.LocalComputeTransfer." + Guid.NewGuid());
        try
        {
            // Own the incoming selection: another fixture may leave a deletion-pending
            // program current, whose name cannot be restored after the next bind.
            using var sentinel = CreateProgram();
            StateCache.Current.UseProgram(sentinel.ProgramId);
            Assert.True(GpuShaderModule.TryCompileGlsl(ShaderType.ComputeShader,
                "#version 430\nlayout(local_size_x=1) in; void main(){}", out var shader, out string compileLog), compileLog);
            using (shader)
            {
                if (contract) shader!.BindingContract = new GpuBindingContract();
                PatchQueries(harmony);
                interfaceQueries = unitQueries = 0;
                Assert.True(GpuComputePipeline.TryCreate(shader!, out var pipeline, out string linkLog), linkLog);
                using (pipeline)
                {
                    Assert.Equal(7, interfaceQueries);
                    Assert.Equal(0, unitQueries);
                    Assert.True(pipeline!.IsValid);
                    using (pipeline.UseScope()) Assert.Equal(pipeline.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
                    Assert.Equal(sentinel.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
                    Assert.Equal(7, interfaceQueries);
                    Assert.Equal(ErrorCode.NoError, GL.GetError());
                }
            }
            StateCache.Current.UseProgram(0);
        }
        finally { harmony.UnpatchAll(harmony.Id); StateCache.Current.UseProgram(0); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>A failed deferred link publishes the real empty executable outcome without making the object unusable.</summary>
    [Fact]
    public void FailedDeferredLinkPublishesUnavailableExecutable()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = GpuProgramObject.Create();
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner));
            StateCache.Current.SubmitProgramLink(owner);
            Assert.False(entry.MetadataAvailable);
            Assert.False(StateCache.Current.CompleteProgramLink(owner, out _));
            Assert.True(entry.MetadataAvailable);
            Assert.False(entry.Metadata.ExecutableAvailable);
            Assert.Empty(entry.Metadata.Resources);
            Assert.Empty(entry.Metadata.TextureUnits);
            Assert.Null(owner.PendingLink);
            Assert.True(owner.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Native link dispatch cannot reenter metadata acquisition before its deferred completion boundary.</summary>
    [Fact]
    public void SubmissionRejectsReentrantAcquisition()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.ProgramSubmissionReentry." + Guid.NewGuid());
        try
        {
            using var owner = GpuProgramObject.Create();
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(manager.GetResourceRegistration(owner));
            var reader = new RejectReader();
            int callbacks = 0;
            duringLink = () =>
            {
                callbacks++;
                Assert.Null(manager.AcquireResource(GpuResourceKind.Program, owner.ProgramId, reader));
                Assert.False(entry.MetadataAvailable);
            };
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.LinkProgram), [typeof(int)]),
                prefix: new HarmonyMethod(typeof(GpuProgramNarrowPublicationTests), nameof(ObserveLink)));
            StateCache.Current.SubmitProgramLink(owner);
            Assert.Equal(1, callbacks);
            Assert.Equal(0, reader.Calls);
            Assert.False(entry.MetadataAvailable);
            Assert.NotNull(owner.PendingLink);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            duringLink = null; harmony.UnpatchAll(harmony.Id);
            fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Exact external unit calls read only the affected unit while preserving the already reflected executable.</summary>
    [Fact]
    public void ExternalUnitUpdateReadsOnlyItsResult()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.ExternalNarrowProgramUnit." + Guid.NewGuid());
        try
        {
            using var owner = CreateProgram();
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner));
            var before = entry.Metadata;
            int location = GL.GetUniformLocation(owner.ProgramId, "inputs[0]");
            var reader = new RejectReader();
            entry.Reader = reader;
            harmony.Patch(AccessTools.Method(typeof(GpuProgramNarrowPublicationTests), nameof(SetExternalUnit)),
                transpiler: new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler)));
            PatchQueries(harmony);
            interfaceQueries = unitQueries = 0;
            SetExternalUnit(owner.ProgramId, location, 4);
            Assert.Equal(0, reader.Calls);
            Assert.Equal(0, interfaceQueries);
            Assert.Equal(1, unitQueries);
            Assert.Equal(before.Resources, entry.Metadata.Resources);
            Assert.Contains(entry.Metadata.TextureUnits, item => item.Location == location && item.Unit == 4);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Deferred completion consumes the latest submission once and performs no status queries on repeated completion.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredCompletionConsumesLatestSubmissionOnce(bool registered)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.DeferredProgramOnce." + Guid.NewGuid());
        int shader = GL.CreateShader(ShaderType.ComputeShader);
        try
        {
            GL.ShaderSource(shader, "#version 430\nlayout(local_size_x=1) in; void main(){}");
            GL.CompileShader(shader);
            using var owner = GpuProgramObject.Create();
            owner.AttachShader(shader);
            StateCache.Current.SubmitProgramLink(owner);
            var first = owner.PendingLink;
            Assert.NotNull(first);
            StateCache.Current.SubmitProgramLink(owner);
            Assert.NotSame(first, owner.PendingLink);
            Assert.True(StateCache.Current.CompleteProgramLink(owner, out string log), log);
            Assert.Null(owner.PendingLink);
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.GetProgram), [typeof(int), typeof(GetProgramParameterName), typeof(int).MakeByRefType()]),
                prefix: new HarmonyMethod(typeof(GpuProgramNarrowPublicationTests), nameof(CountInterface)));
            interfaceQueries = 0;
            Assert.False(StateCache.Current.CompleteProgramLink(owner, out _));
            Assert.Equal(0, interfaceQueries);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id); StateCache.Current.DeleteShader(shader);
            fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Controlled sampler edits retain unrelated interface/unit facts and preserve named or restoring dispatch behavior.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ControlledUnitUpdateDoesNotRediscoverExecutable(bool direct)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.NarrowProgramUnit." + Guid.NewGuid());
        try
        {
            using var owner = CreateProgram();
            using var sentinel = CreateProgram();
            using var capability = new SeparateShaderObjectsCapabilityScope(direct);
            StateCache.Current.UseProgram(sentinel.ProgramId);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner));
            var before = entry.Metadata;
            var reader = new RejectReader();
            entry.Reader = reader;
            int location = GL.GetUniformLocation(owner.ProgramId, "inputs[0]");
            int otherLocation = GL.GetUniformLocation(owner.ProgramId, "inputs[1]");
            Assert.True(location >= 0 && otherLocation >= 0);
            PatchQueries(harmony);
            interfaceQueries = unitQueries = 0;
            ulong revision = entry.MetadataRevision;
            StateCache.Current.SetProgramUniformUnit(owner.ProgramId, location, 3);
            Assert.Equal(0, reader.Calls);
            Assert.Equal(0, interfaceQueries);
            Assert.Equal(0, unitQueries);
            Assert.Equal(revision + 1, entry.MetadataRevision);
            Assert.Equal(before.Resources, entry.Metadata.Resources);
            Assert.Contains(entry.Metadata.TextureUnits, item => item.Location == location && item.Unit == 3);
            Assert.Equal(before.TextureUnits.Single(item => item.Location == otherLocation),
                entry.Metadata.TextureUnits.Single(item => item.Location == otherLocation));
            revision = entry.MetadataRevision;
            StateCache.Current.SetProgramUniformUnit(owner.ProgramId, location, 3);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(0, unitQueries);
            Assert.Equal(sentinel.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            GL.GetUniform(owner.ProgramId, location, out int native);
            Assert.Equal(3, native);
            Assert.Equal(1, unitQueries);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id); StateCache.Current.UseProgram(0);
            fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Managed block binding edits preserve executable collections and avoid rerunning the canonical reader.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ControlledBlockUpdateDoesNotReflectInterface(bool storage)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.NarrowProgramBlock." + Guid.NewGuid());
        try
        {
            using var owner = CreateProgram();
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner));
            var before = entry.Metadata;
            var kind = storage ? ProgramInterface.ShaderStorageBlock : ProgramInterface.UniformBlock;
            int index = GL.GetProgramResourceIndex(owner.ProgramId, kind, storage ? "Output" : "Parameters");
            Assert.True(index >= 0);
            var reader = new RejectReader();
            entry.Reader = reader;
            PatchQueries(harmony);
            interfaceQueries = unitQueries = 0;
            if (storage) StateCache.Current.ShaderStorageBlockBinding(owner.ProgramId, index, 3);
            else StateCache.Current.UniformBlockBinding(owner.ProgramId, index, 3);
            Assert.Equal(0, reader.Calls);
            Assert.Equal(0, interfaceQueries);
            Assert.Equal(0, unitQueries);
            Assert.Equal(before.TextureUnits, entry.Metadata.TextureUnits);
            var changed = entry.Metadata.Resources.Single(item => item.Interface == kind && item.Index == index);
            Assert.Equal(3, changed.Properties[ProgramProperty.BufferBinding][0]);
            Assert.All(before.Resources.Where(item => item.Interface != kind || item.Index != index),
                item => Assert.Contains(item, entry.Metadata.Resources));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Counts native setters to prove managed validation happens before driver dispatch.</summary>
    private static void CountAssignment() => assignmentCalls++;

    /// <summary>Runs a deliberately reentrant query attempt during actual native link dispatch.</summary>
    private static void ObserveLink() => duringLink?.Invoke();

    /// <summary>Retains a concrete native call site for the production compiler-checked adapter map.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SetExternalUnit(int program, int location, int unit) => GL.ProgramUniform1(program, location, unit);

    /// <summary>Compiles an interface with multiple units and both block types so preservation checks cover unrelated facts.</summary>
    private static GpuProgramObject CreateProgram()
    {
        int shader = GL.CreateShader(ShaderType.ComputeShader);
        var candidate = GpuProgramObject.Create();
        try
        {
            GL.ShaderSource(shader, "#version 430\nlayout(local_size_x=1) in; uniform sampler2D inputs[2]; layout(std140,binding=0) uniform Parameters { vec4 parameter; }; layout(std430,binding=0) buffer Output { vec4 result; }; void main(){ result=texelFetch(inputs[0],ivec2(0),0)+texelFetch(inputs[1],ivec2(0),0)+parameter; }");
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
            Assert.Equal(1, compiled);
            candidate.AttachShader(shader);
            Assert.True(candidate.TryLink(out string? error), error);
            candidate.DetachShader(shader);
            return candidate;
        }
        catch { candidate.Dispose(); throw; }
        finally { StateCache.Current.DeleteShader(shader); }
    }

    /// <summary>Observes interface enumeration and integer unit reads at their exact native signatures.</summary>
    private static void PatchQueries(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.GetProgramInterface), [typeof(int), typeof(ProgramInterface), typeof(ProgramInterfaceParameter), typeof(int).MakeByRefType()]),
            prefix: new HarmonyMethod(typeof(GpuProgramNarrowPublicationTests), nameof(CountInterface)));
        harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.GetUniform), [typeof(int), typeof(int), typeof(int).MakeByRefType()]),
            prefix: new HarmonyMethod(typeof(GpuProgramNarrowPublicationTests), nameof(CountUnit)));
    }

    /// <summary>Counts interface enumeration without replacing native results.</summary>
    private static void CountInterface() => interfaceQueries++;
    /// <summary>Counts integer uniform reads without replacing native results.</summary>
    private static void CountUnit() => unitQueries++;

    /// <summary>Rejects full executable recovery while a narrow command has all unrelated canonical facts.</summary>
    private sealed class RejectReader : IGpuResourceMetadataReader<GpuProgramMetadata>
    {
        internal int Calls;
        /// <inheritdoc />
        public bool IsResource(nint id) { Calls++; throw new InvalidOperationException("Unexpected program validity recovery."); }
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuProgramMetadata metadata) { Calls++; throw new InvalidOperationException("Unexpected full program reflection."); }
    }
    #endregion
}

/// <summary>Checks deferred executable completion against an independently retired context generation.</summary>
[Collection("OperationContext")]
[Trait("Category", "GPU")]
public sealed class GpuProgramDeferredContextTests(HeadlessGLFixture fixture)
{
    private static int queries;

    #region Public API
    /// <summary>Completion after context retirement refuses driver access and cannot publish into a replacement context.</summary>
    [Fact]
    public void RetiredContextRejectsDeferredCompletionBeforeQueries()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.DeferredProgramContext." + Guid.NewGuid());
        int native = 0;
        try
        {
            using var owner = GpuProgramObject.Create();
            native = owner.ProgramId;
            StateCache.Current.SubmitProgramLink(owner);
            var entry = GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner)!;
            RenderContextRegistry.Retire(fixture);
            RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.GetProgram), [typeof(int), typeof(GetProgramParameterName), typeof(int).MakeByRefType()]),
                prefix: new HarmonyMethod(typeof(GpuProgramDeferredContextTests), nameof(CountQuery)));
            queries = 0;
            Assert.False(StateCache.Current.CompleteProgramLink(owner, out _));
            Assert.Equal(0, queries);
            Assert.Equal(GpuResourceRegistrationState.ContextRetired, entry.State);
            Assert.False(entry.MetadataAvailable);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            if (native != 0 && GL.IsProgram(native)) StateCache.Current.DeleteProgram(native);
            fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Counts status access to establish that stale completion never queries a native name.</summary>
    private static void CountQuery() => queries++;
    #endregion
}
