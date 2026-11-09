using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using System.IO;
using System.Linq;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

/// <summary>Checks executable candidate ownership, frozen inputs and fixed generation invalidation.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class RHIShaderCreationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Backend extraction and installation preserve the prepared interface without sharing native ownership.</summary>
    [Fact]
    public void ExecutableBinaryRoundTripCreatesAnIndependentGeneration()
    {
        // Arrange: request a retrievable executable binary from a prepared compute shader.
        Start();
        try
        {
            var description = RHIShaderInputs.ComputeDescription();
            var retrievable = new RHIShaderDescription(description.Kind, description.Stages, description.Bindings,
                retrievableBinary: true);
            using var original = RHICommandContext.Current.CreateShader(retrievable).RequireResource();
            // Act: export the first executable and install it as a second shader generation.
            var binary = RHICommandContext.Current.ExportShaderBinary(original, 16 * 1024 * 1024);
            // Assert: the installed binary has independent identity and retains the prepared interface.
            Assert.NotNull(binary);
            using var restored = RHICommandContext.Current.CreateShader(new(description.Kind, description.Stages,
                description.Bindings, binary)).RequireResource();
            Assert.NotEqual(original.Program.ProgramId, restored.Program.ProgramId);
            Assert.Equal(original.Interface.PreparedBindings.Entries.Count, restored.Interface.PreparedBindings.Entries.Count);
            original.Dispose();
            RHIShaderRef reference = restored;
            Assert.Same(restored, reference.RequireShader());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }
    /// <summary>Ready creation retains a prepared interface and rejects use after external executable mutation.</summary>
    [Fact]
    public void ReadyShaderRetainsOneFixedExecutableGeneration()
    {
        // Arrange: create a ready executable owner and retain its typed reference.
        Start();
        try
        {
            using var shader = RHICommandContext.Current.CreateShader(RHIShaderInputs.ComputeDescription()).RequireResource();
            RHIShaderRef reference = shader;
            Assert.Same(shader, reference.RequireShader());
            Assert.True(shader.Program.MetadataSnapshot.ExecutableAvailable);
            GL.GetProgram(shader.Program.ProgramId, GetProgramParameterName.AttachedShaders, out int attached);
            Assert.Equal(0, attached);
            // External relinking invalidates the captured executable even if a numeric name stays unchanged.
            // Act: perform an external relink without changing the numeric program name.
            GL.LinkProgram(shader.Program.ProgramId);
            // Assert: the captured executable generation is no longer usable.
            Assert.Throws<InvalidOperationException>(() => reference.RequireShader());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }
    /// <summary>Pending cancellation cannot expose an owner; successful completion transfers that owner exactly once.</summary>
    [Fact]
    public void PendingCreationCompletesOrCancelsWithoutExposingPartialOwnership()
    {
        // Arrange: begin asynchronous creation without exposing a partially constructed owner.
        Start();
        try
        {
            using var pending = RHICommandContext.Current.BeginCreateShader(RHIShaderInputs.ComputeDescription());
            // Assert: no partial owner is exposed before asynchronous work reaches a terminal state.
            Assert.False(pending.Result.IsSuccess && pending.Status != CompletionStatus.Ready);
            // Act: cancel pending work or accept the already completed candidate, then retire a fresh owner.
            if (pending.Status == CompletionStatus.Pending)
            {
                pending.Cancel();
                Assert.Equal(CompletionStatus.Cancelled, pending.TryComplete());
                Assert.False(pending.Result.IsSuccess);
                pending.Dispose();
            }
            else using (pending.Result.RequireResource()) Assert.Equal(CompletionStatus.Ready, pending.Status);
            using var completed = RHICommandContext.Current.CreateShader(RHIShaderInputs.ComputeDescription()).RequireResource();
            RHIShaderRef reference = completed;
            completed.Dispose();
            Assert.Throws<ObjectDisposedException>(() => reference.RequireShader());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }
    #endregion

    #region Private
    /// <summary>Starts an isolated native/manager interval for candidate ownership checks.</summary>
    private void Start() { fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); OpenGlStateCache.Current.InvalidateAll(); }
    /// <summary>Drains original-context ownership and clears fixture-external selector knowledge.</summary>
    private void Finish() { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    #endregion
}
