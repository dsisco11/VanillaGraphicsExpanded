using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using OpenTK.Graphics.OpenGL;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

/// <summary>Checks established executable edits publish only their affected facts without full reflection.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuProgramNarrowPublicationTests(HeadlessGLFixture fixture)
{

    #region Public API
    #region Local shader realization
    /// <summary>Context-local shader and PSO owners reuse their canonical interface after native binary or SPIR-V loading.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalComputeRealizationReusesCanonicalInterface(bool binary)
    {
        // Arrange
        fixture.EnsureContextValid();
        fixture.RetireResourceServices();
        Assert.Null(RHIResourceManagerSystem.CurrentManager);
        var context = RHICommandContext.Current;
        var input = RHIShaderInputs.ComputeDescription();
        // Act
        using var first = context.CreateShader(new(input.Kind, input.Stages, input.Bindings, retrievableBinary: binary)).RequireResource();
        using var restored = binary ? context.CreateShader(new(input.Kind, input.Stages, input.Bindings,
            context.ExportShaderBinary(first, 16 * 1024 * 1024))).RequireResource() : null;
        var shader = restored ?? first;
        using var observation = new ProgramAcquisitionCapture();
        using var pipeline = context.CreateComputePipeline(new(shader)).RequireResource();
        var layout = new GpuProgramLayout();
        layout.InstallFixedExecutable(shader);
        // Assert
        Assert.Same(shader.Interface, layout.BinaryInterface);
        Assert.Same(shader, pipeline.RequireShader());
        Assert.Equal(0, observation.Reads);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    #endregion

    #region Layout publication
    /// <summary>External layout application reuses canonical facts; subsequent layout reads never query native state.</summary>
    [Fact]
    public void LayoutAssignmentsAndReadsReuseCanonicalFacts()
    {
        // Arrange
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
                // Act
                layout.ApplyContract(owner.ProgramId);
                layout.ApplyContract(owner.ProgramId);
                // Assert
                Assert.Equal(expectedLocation, layout.ResolveUniformLocation(owner.ProgramId, "inputs").Value);
                Assert.True(layout.ResolveUniformBlockActive(owner.ProgramId, "Parameters").Value);
                // Act: rebuild only the derived cache after contract application.
                layout.RebuildCache(owner.ProgramId);
                // Assert: the canonical acquisition capture confirms rebuild reused existing facts.
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
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = CreateProgram();
            // Act
            Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.ApplyProgramBindings(owner.ProgramId,
                [new(ProgramResourceKind.Uniform, "missing", 1, true), new(ProgramResourceKind.Uniform, "inputs", 2, true)],
                message => OpenGLProgramAssignments.SetUnit(owner.ProgramId, GL.GetUniformLocation(owner.ProgramId, "inputs[0]"), 4)));
            // Assert
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
    public unsafe void InvalidControlledAssignmentsPreserveMetadata(int family)
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = CreateProgram();
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(
                RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner));
            var previous = entry.Metadata;
            ulong revision = entry.MetadataRevision;
            int location = GL.GetUniformLocation(owner.ProgramId, "inputs[0]");
            int index = family == 0 ? 0 : GL.GetProgramResourceIndex(owner.ProgramId,
                family == 1 ? ProgramInterface.UniformBlock : ProgramInterface.ShaderStorageBlock,
                family == 1 ? "Parameters" : "Output");
            using var native = new NativeDriverCapture();
            int assignmentCalls = 0;
            foreach (string symbol in new[] { "glProgramUniform1i", "glUniformBlockBinding", "glShaderStorageBlockBinding" })
            {
                var original = (delegate* unmanaged[Stdcall]<uint, int, int, void>)native.Original(symbol);
                native.Add(symbol, new NativeAssignment((program, block, value) =>
                {
                    original(program, block, value);
                    assignmentCalls++;
                }));
            }
            native.Install();
            int limit = family switch
            {
                0 => GpuSupport.Graphics.MaxCombinedTextureImageUnits,
                1 => GpuSupport.Graphics.MaxUniformBufferBindings,
                _ => GpuSupport.Graphics.MaxShaderStorageBufferBindings
            };
            // Act
            foreach (int value in new[] { -1, limit })
            {
                if (family == 0) Assert.Throws<ArgumentOutOfRangeException>(() => OpenGLProgramAssignments.SetUnit(owner.ProgramId, location, value));
                else if (family == 1) Assert.Throws<ArgumentOutOfRangeException>(() => OpenGLProgramAssignments.UniformBlockBinding(owner.ProgramId, index, value));
                else Assert.Throws<ArgumentOutOfRangeException>(() => OpenGLProgramAssignments.ShaderStorageBlockBinding(owner.ProgramId, index, value));
            }
            // Assert
            Assert.Equal(0, assignmentCalls);
            Assert.Equal(previous, entry.Metadata);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.True(entry.MetadataAvailable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    #endregion

    #region Deferred executable completion
    /// <summary>A failed deferred link publishes the real empty executable outcome without making the object unusable.</summary>
    [Fact]
    public void FailedDeferredLinkPublishesUnavailableExecutable()
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var executionBackend = new VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBackend(OpenGlStateCache.Current);
        try
        {
            using var owner = NativeResourceSetup.CreateProgram();
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(
                RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner));
            // Act
            executionBackend.SubmitProgramExecutable(owner);
            Assert.False(entry.MetadataAvailable);
            Assert.False(executionBackend.CompleteProgramLink(owner, out _));
            // Assert
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
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var executionBackend = new VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBackend(OpenGlStateCache.Current);
        try
        {
            using var owner = NativeResourceSetup.CreateProgram();
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(manager.GetResourceRegistration(owner));
            var reader = new RejectReader();
            int callbacks = 0;
            Action duringLink = () =>
            {
                callbacks++;
                Assert.Null(manager.AcquireResource(GpuResourceKind.Program, owner.ProgramId, reader));
                Assert.False(entry.MetadataAvailable);
            };
            using var native = new NativeDriverCapture();
            nint original = native.Original("glLinkProgram");
            native.Add("glLinkProgram", new NativeLink(program =>
            {
                native.Observe(duringLink);
                ((delegate* unmanaged[Stdcall]<uint, void>)original)(program);
            }));
            native.Install();
            // Act
            executionBackend.SubmitProgramExecutable(owner);
            native.RequireSuccessfulObservation();
            // Assert
            Assert.Equal(1, callbacks);
            Assert.Equal(0, reader.Calls);
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll();
        }
    }

    #endregion

    #region Narrow metadata assignments
    /// <summary>Exact external unit calls publish supplied values without querying the already reflected executable.</summary>
    [Fact]
    public void ExternalUnitUpdatePublishesSuppliedValue()
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = CreateProgram();
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(
                RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner));
            var before = entry.Metadata;
            int location = GL.GetUniformLocation(owner.ProgramId, "inputs[0]");
            var reader = new RejectReader();
            entry.Reader = reader;

            using var queries = new ExecutableDescriptorCapture();
            // Act
            SetExternalUnit(owner.ProgramId, location, 4);
            // Assert
            Assert.Equal(0, reader.Calls);
            Assert.Equal(0, queries.InterfaceReads);
            Assert.Equal(0, queries.UnitReads);
            Assert.Equal(before.Resources, entry.Metadata.Resources);
            Assert.Contains(entry.Metadata.TextureUnits, item => item.Location == location && item.Unit == 4);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Deferred completion consumes the latest submission once and performs no status queries on repeated completion.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredCompletionConsumesLatestSubmissionOnce(bool registered)
    {
        // Arrange
        fixture.EnsureContextValid();
        var executionBackend = new VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBackend(OpenGlStateCache.Current);
        if (registered) fixture.InitializeResourceDisposal();
        int shader = GL.CreateShader(ShaderType.ComputeShader);
        try
        {
            GL.ShaderSource(shader, "#version 430\nlayout(local_size_x=1) in; void main(){}");
            GL.CompileShader(shader);
            using var owner = NativeResourceSetup.CreateProgram();
            GL.AttachShader(owner.ProgramId, shader);
            // Act
            executionBackend.SubmitProgramExecutable(owner);
            executionBackend.SubmitProgramExecutable(owner);
            Assert.True(executionBackend.CompleteProgramLink(owner, out string log), log);
            using var queries = new ExecutableDescriptorCapture();
            Assert.False(executionBackend.CompleteProgramLink(owner, out _));
            // Assert
            Assert.Equal(0, queries.ProgramReads);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Controlled sampler edits retain unrelated interface/unit facts and preserve named or restoring dispatch behavior.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ControlledUnitUpdateDoesNotRediscoverExecutable(bool direct)
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = CreateProgram();
            using var sentinel = CreateProgram();
            using var capability = new SeparateShaderObjectsCapabilityScope(direct);
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, sentinel.ProgramId);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(
                RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner));
            var before = entry.Metadata;
            var reader = new RejectReader();
            entry.Reader = reader;
            int location = GL.GetUniformLocation(owner.ProgramId, "inputs[0]");
            int otherLocation = GL.GetUniformLocation(owner.ProgramId, "inputs[1]");
            Assert.True(location >= 0 && otherLocation >= 0);
            using var queries = new ExecutableDescriptorCapture();
            ulong revision = entry.MetadataRevision;
            // Act
            OpenGLProgramAssignments.SetUnit(owner.ProgramId, location, 3);
            // Assert
            Assert.Equal(0, reader.Calls);
            Assert.Equal(0, queries.InterfaceReads);
            Assert.Equal(0, queries.UnitReads);
            Assert.Equal(revision + 1, entry.MetadataRevision);
            Assert.Equal(before.Resources, entry.Metadata.Resources);
            Assert.Contains(entry.Metadata.TextureUnits, item => item.Location == location && item.Unit == 3);
            Assert.Equal(before.TextureUnits.Single(item => item.Location == otherLocation),
                entry.Metadata.TextureUnits.Single(item => item.Location == otherLocation));
            revision = entry.MetadataRevision;
            OpenGLProgramAssignments.SetUnit(owner.ProgramId, location, 3);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(0, queries.UnitReads);
            Assert.Equal(sentinel.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            OpenTK.Graphics.OpenGL4.GL.GetUniform(owner.ProgramId, location, out int native);
            Assert.Equal(3, native);
            Assert.Equal(1, queries.UnitReads);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
            fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Managed block binding edits preserve executable collections and avoid rerunning the canonical reader.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ControlledBlockUpdateDoesNotReflectInterface(bool storage)
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = CreateProgram();
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(
                RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner));
            var before = entry.Metadata;
            var kind = storage ? ProgramInterface.ShaderStorageBlock : ProgramInterface.UniformBlock;
            int index = GL.GetProgramResourceIndex(owner.ProgramId, kind, storage ? "Output" : "Parameters");
            Assert.True(index >= 0);
            var reader = new RejectReader();
            entry.Reader = reader;
            using var queries = new ExecutableDescriptorCapture();
            // Act
            if (storage) OpenGLProgramAssignments.ShaderStorageBlockBinding(owner.ProgramId, index, 3);
            else OpenGLProgramAssignments.UniformBlockBinding(owner.ProgramId, index, 3);
            // Assert
            Assert.Equal(0, reader.Calls);
            Assert.Equal(0, queries.InterfaceReads);
            Assert.Equal(0, queries.UnitReads);
            Assert.Equal(before.TextureUnits, entry.Metadata.TextureUnits);
            var changed = entry.Metadata.Resources.Single(item => item.Interface == (kind == ProgramInterface.UniformBlock ? ProgramResourceKind.UniformBlock : ProgramResourceKind.ShaderStorageBlock) && item.Index == index);
            Assert.Equal(3, changed.BufferBinding);
            Assert.All(before.Resources.Where(item => item.Interface != (kind == ProgramInterface.UniformBlock ? ProgramResourceKind.UniformBlock : ProgramResourceKind.ShaderStorageBlock) || item.Index != index),
                item => Assert.Contains(item, entry.Metadata.Resources));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Matches native executable link submission.</summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate void NativeLink(uint program);
    /// <summary>Preserves the three 32-bit arguments of represented assignment commands.</summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate void NativeAssignment(uint program, int index, int value);
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

    /// <summary>Rejects full executable recovery while a narrow command has all unrelated canonical facts.</summary>
    private sealed class RejectReader : IGpuResourceMetadataReader<GpuProgramMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuProgramMetadata metadata) { Calls++; throw new InvalidOperationException("Unexpected full program reflection."); }
        #endregion
        #region Test state
        internal int Calls;
        #endregion
    }
    #endregion
}
