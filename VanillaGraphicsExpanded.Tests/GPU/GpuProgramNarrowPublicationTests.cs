using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
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
    /// <summary>External layout application reuses canonical facts; subsequent layout reads never query native state.</summary>
    [Fact]
    public void LayoutAssignmentsAndReadsReuseCanonicalFacts()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = CreateProgram();
            var layout = GpuProgramLayout.TryBuild(owner.ProgramId);
            layout.RegisterSamplerUnitArray("inputs", 2, 2);
            layout.RegisterUniformBlockBinding("Parameters", 1);
            layout.RegisterShaderStorageBlockBinding("Output", 2);
            int expectedLocation = GL.GetUniformLocation(owner.ProgramId, "inputs");
            using (var capture = new ProgramAcquisitionCapture())
            {
                capture.Calibrate(owner.ProgramId);
                layout.ApplyContract(owner.ProgramId);
                layout.ApplyContract(owner.ProgramId);
                Assert.Equal(expectedLocation, layout.ResolveUniformLocation(owner.ProgramId, "inputs").Value);
                Assert.True(layout.ResolveUniformBlockActive(owner.ProgramId, "Parameters").Value);
                layout.RebuildCache(owner.ProgramId);
                Assert.Equal(0, capture.Reads);
            }
            GL.GetUniform(owner.ProgramId, GL.GetUniformLocation(owner.ProgramId, "inputs[0]"), out int first);
            GL.GetUniform(owner.ProgramId, GL.GetUniformLocation(owner.ProgramId, "inputs[1]"), out int second);
            Assert.Equal(2, first); Assert.Equal(3, second);
            Assert.Equal(1, owner.MetadataSnapshot.Resources.Single(item => item.Interface == ProgramResourceKind.UniformBlock).BufferBinding);
            Assert.Equal(2, owner.MetadataSnapshot.Resources.Single(item => item.Interface == ProgramResourceKind.ShaderStorageBlock).BufferBinding);
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>A warning callback cannot change interface facts and then apply the remaining stale assignment packet.</summary>
    [Fact]
    public void LayoutAssignmentsRejectInterfaceChangeDuringCallback()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = CreateProgram();
            Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.ApplyProgramBindings(owner.ProgramId,
                [new(ProgramResourceKind.Uniform, "missing", 1, true), new(ProgramResourceKind.Uniform, "inputs", 2, true)],
                message => OpenGLProgramAssignments.SetUnit(owner.ProgramId, GL.GetUniformLocation(owner.ProgramId, "inputs[0]"), 4)));
            GL.GetUniform(owner.ProgramId, GL.GetUniformLocation(owner.ProgramId, "inputs[0]"), out int unit);
            Assert.Equal(4, unit);
        }
        finally { fixture.CleanupGpuResources(); }
    }

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
                if (family == 0) Assert.Throws<ArgumentOutOfRangeException>(() => OpenGLProgramAssignments.SetUnit(owner.ProgramId, location, value));
                else if (family == 1) Assert.Throws<ArgumentOutOfRangeException>(() => OpenGLProgramAssignments.UniformBlockBinding(owner.ProgramId, index, value));
                else Assert.Throws<ArgumentOutOfRangeException>(() => OpenGLProgramAssignments.ShaderStorageBlockBinding(owner.ProgramId, index, value));
            }
            Assert.Equal(0, assignmentCalls);
            Assert.Equal(previous, entry.Metadata);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.True(entry.MetadataAvailable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Context-local shader and PSO owners reuse their canonical interface without another native resource transfer.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalComputeRealizationReusesCanonicalInterface(bool binary)
    {
        fixture.EnsureContextValid();
        fixture.UseContextLocalResources();
        Assert.Null(GpuResourceManagerSystem.CurrentManager);
        var context = RHICommandContext.Current;
        var input = RHIShaderInputs.ComputeDescription();
        using var first = context.CreateShader(new(input.Kind, input.Stages, input.Bindings, retrievableBinary: binary)).RequireResource();
        using var restored = binary ? context.CreateShader(new(input.Kind, input.Stages, input.Bindings,
            context.ExportShaderBinary(first, 16 * 1024 * 1024))).RequireResource() : null;
        var shader = restored ?? first;
        using var observation = new ProgramAcquisitionCapture();
        using var pipeline = context.CreateComputePipeline(new(shader)).RequireResource();
        var layout = new GpuProgramLayout();
        layout.InstallFixedExecutable(shader);
        Assert.Same(shader.Interface, layout.BinaryInterface);
        Assert.Same(shader, pipeline.RequireShader());
        Assert.Equal(0, observation.Reads);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A failed deferred link publishes the real empty executable outcome without making the object unusable.</summary>
    [Fact]
    public void FailedDeferredLinkPublishesUnavailableExecutable()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var executionBackend = new VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBackend(OpenGlStateCache.Current);
        try
        {
            using var owner = NativeResourceSetup.CreateProgram();
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner));
            executionBackend.SubmitProgramExecutable(owner);
            Assert.False(entry.MetadataAvailable);
            Assert.False(executionBackend.CompleteProgramLink(owner, out _));
            Assert.True(entry.MetadataAvailable);
            Assert.False(entry.Metadata.ExecutableAvailable);
            Assert.Empty(entry.Metadata.Resources);
            Assert.Empty(entry.Metadata.TextureUnits);
            Assert.True(owner.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Native link dispatch cannot reenter metadata acquisition before its deferred completion boundary.</summary>
    [Fact]
    public unsafe void SubmissionRejectsReentrantAcquisition()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var executionBackend = new VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBackend(OpenGlStateCache.Current);
        var harmony = new Harmony("Tests.ProgramSubmissionReentry." + Guid.NewGuid());
        try
        {
            using var owner = NativeResourceSetup.CreateProgram();
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
            using var native = new NativeDriverCapture();
            nint original = native.Original("glLinkProgram");
            native.Add("glLinkProgram", new NativeLink(program =>
            {
                native.Observe(ObserveLink);
                ((delegate* unmanaged[Stdcall]<uint, void>)original)(program);
            }));
            native.Install();
            executionBackend.SubmitProgramExecutable(owner);
            Assert.Equal(1, callbacks);
            Assert.Equal(0, reader.Calls);
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            duringLink = null; harmony.UnpatchAll(harmony.Id);
            fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Exact external unit calls publish supplied values without querying the already reflected executable.</summary>
    [Fact]
    public void ExternalUnitUpdatePublishesSuppliedValue()
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

            PatchQueries(harmony);
            interfaceQueries = unitQueries = 0;
            SetExternalUnit(owner.ProgramId, location, 4);
            Assert.Equal(0, reader.Calls);
            Assert.Equal(0, interfaceQueries);
            Assert.Equal(0, unitQueries);
            Assert.Equal(before.Resources, entry.Metadata.Resources);
            Assert.Contains(entry.Metadata.TextureUnits, item => item.Location == location && item.Unit == 4);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Deferred completion consumes the latest submission once and performs no status queries on repeated completion.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredCompletionConsumesLatestSubmissionOnce(bool registered)
    {
        fixture.EnsureContextValid();
        var executionBackend = new VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBackend(OpenGlStateCache.Current);
        if (registered) fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.DeferredProgramOnce." + Guid.NewGuid());
        int shader = GL.CreateShader(ShaderType.ComputeShader);
        try
        {
            GL.ShaderSource(shader, "#version 430\nlayout(local_size_x=1) in; void main(){}");
            GL.CompileShader(shader);
            using var owner = NativeResourceSetup.CreateProgram();
            GL.AttachShader(owner.ProgramId, shader);
            executionBackend.SubmitProgramExecutable(owner);
            executionBackend.SubmitProgramExecutable(owner);
            Assert.True(executionBackend.CompleteProgramLink(owner, out string log), log);
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.GetProgram), [typeof(int), typeof(GetProgramParameterName), typeof(int).MakeByRefType()]),
                prefix: new HarmonyMethod(typeof(GpuProgramNarrowPublicationTests), nameof(CountInterface)));
            interfaceQueries = 0;
            Assert.False(executionBackend.CompleteProgramLink(owner, out _));
            Assert.Equal(0, interfaceQueries);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll();
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
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, sentinel.ProgramId);
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
            OpenGLProgramAssignments.SetUnit(owner.ProgramId, location, 3);
            Assert.Equal(0, reader.Calls);
            Assert.Equal(0, interfaceQueries);
            Assert.Equal(0, unitQueries);
            Assert.Equal(revision + 1, entry.MetadataRevision);
            Assert.Equal(before.Resources, entry.Metadata.Resources);
            Assert.Contains(entry.Metadata.TextureUnits, item => item.Location == location && item.Unit == 3);
            Assert.Equal(before.TextureUnits.Single(item => item.Location == otherLocation),
                entry.Metadata.TextureUnits.Single(item => item.Location == otherLocation));
            revision = entry.MetadataRevision;
            OpenGLProgramAssignments.SetUnit(owner.ProgramId, location, 3);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(0, unitQueries);
            Assert.Equal(sentinel.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            OpenTK.Graphics.OpenGL4.GL.GetUniform(owner.ProgramId, location, out int native);
            Assert.Equal(3, native);
            Assert.Equal(1, unitQueries);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id); OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
            fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll();
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
            if (storage) OpenGLProgramAssignments.ShaderStorageBlockBinding(owner.ProgramId, index, 3);
            else OpenGLProgramAssignments.UniformBlockBinding(owner.ProgramId, index, 3);
            Assert.Equal(0, reader.Calls);
            Assert.Equal(0, interfaceQueries);
            Assert.Equal(0, unitQueries);
            Assert.Equal(before.TextureUnits, entry.Metadata.TextureUnits);
            var changed = entry.Metadata.Resources.Single(item => item.Interface == (kind == ProgramInterface.UniformBlock ? ProgramResourceKind.UniformBlock : ProgramResourceKind.ShaderStorageBlock) && item.Index == index);
            Assert.Equal(3, changed.BufferBinding);
            Assert.All(before.Resources.Where(item => item.Interface != (kind == ProgramInterface.UniformBlock ? ProgramResourceKind.UniformBlock : ProgramResourceKind.ShaderStorageBlock) || item.Index != index),
                item => Assert.Contains(item, entry.Metadata.Resources));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Counts native setters to prove managed validation happens before driver dispatch.</summary>
    private static void CountAssignment() => assignmentCalls++;

    /// <summary>Matches native executable link submission.</summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate void NativeLink(uint program);
    /// <summary>Runs a deliberately reentrant query attempt during actual native link dispatch.</summary>
    private static void ObserveLink() => duringLink?.Invoke();

    /// <summary>Retains a concrete native call site for the production compiler-checked adapter map.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SetExternalUnit(int program, int location, int unit) => GL.ProgramUniform1(program, location, unit);

    /// <summary>Compiles an interface with multiple units and both block types so preservation checks cover unrelated facts.</summary>
    private static GpuProgramObject CreateProgram()
    {
        int shader = GL.CreateShader(ShaderType.ComputeShader);
        var candidate = NativeResourceSetup.CreateProgram();
        try
        {
            GL.ShaderSource(shader, "#version 430\nlayout(local_size_x=1) in; uniform sampler2D inputs[2]; layout(std140,binding=0) uniform Parameters { vec4 parameter; }; layout(std430,binding=0) buffer Output { vec4 result; }; void main(){ result=texelFetch(inputs[0],ivec2(0),0)+texelFetch(inputs[1],ivec2(0),0)+parameter; }");
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
            Assert.Equal(1, compiled);
            GL.AttachShader(candidate.ProgramId, shader);
            Assert.True(NativeProgramFixture.Link(candidate, out string? error), error);
            GL.DetachShader(candidate.ProgramId, shader);
            return candidate;
        }
        catch { candidate.Dispose(); throw; }
        finally { VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader)); }
    }

    /// <summary>Observes interface enumeration and integer unit reads at their exact native signatures.</summary>
    private static void PatchQueries(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.GetProgramInterface), [typeof(int), typeof(ProgramInterface), typeof(ProgramInterfaceParameter), typeof(int).MakeByRefType()]),
            prefix: new HarmonyMethod(typeof(GpuProgramNarrowPublicationTests), nameof(CountInterface)));
        harmony.Patch(AccessTools.Method(typeof(OpenTK.Graphics.OpenGL4.GL), nameof(GL.GetUniform), [typeof(int), typeof(int), typeof(int).MakeByRefType()]),
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
        var executionBackend = new VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBackend(OpenGlStateCache.Current);
        var harmony = new Harmony("Tests.DeferredProgramContext." + Guid.NewGuid());
        int native = 0;
        try
        {
            using var owner = NativeResourceSetup.CreateProgram();
            native = owner.ProgramId;
            executionBackend.SubmitProgramExecutable(owner);
            var entry = GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner)!;
            RenderContextRegistry.Retire(fixture);
            RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.GetProgram), [typeof(int), typeof(GetProgramParameterName), typeof(int).MakeByRefType()]),
                prefix: new HarmonyMethod(typeof(GpuProgramDeferredContextTests), nameof(CountQuery)));
            harmony.Patch(AccessTools.Method(typeof(OpenTK.Graphics.OpenGL4.GL), nameof(GL.GetProgram), [typeof(int), typeof(OpenTK.Graphics.OpenGL4.GetProgramParameterName), typeof(int).MakeByRefType()]),
                prefix: new HarmonyMethod(typeof(GpuProgramDeferredContextTests), nameof(CountQuery)));
            queries = 0;
            Assert.Throws<InvalidOperationException>(() => executionBackend.CompleteProgramLink(owner, out _));
            Assert.Equal(0, queries);
            Assert.Equal(GpuResourceRegistrationState.ContextRetired, entry.State);
            Assert.False(entry.MetadataAvailable);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            if (native != 0 && GL.IsProgram(native)) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Program, native));
            fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Counts status access to establish that stale completion never queries a native name.</summary>
    private static void CountQuery() => queries++;
    #endregion
}
