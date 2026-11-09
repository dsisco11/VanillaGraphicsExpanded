using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;


namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks optional owner labels without giving wrappers native diagnostic authority.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class RHIResourceLabelTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    private static int labelAttempts;

    #region Public API
    /// <summary>Labels preserve supplied names, selectors and pending errors without native object probes.</summary>
    [Fact]
    public void OwnerLabelPreservesStateAndPendingErrors()
    {
        // Arrange: capture the owner selector and queue an unrelated native error.
        EnsureContextValid();
        bool succeeded = true;
        try
        {
            using var owner = RHICommandContext.Current.CreateBuffer(new RHIBufferDescription
            {
                Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 16,
                Usage = BufferUsage.StaticDraw
            }).RequireResource();
            GL.GetInteger(GetPName.ArrayBufferBinding, out int binding);
            // The invalid viewport creates an unrelated pending error. A label must never consume it.
            // Act: label a live allocation while the unrelated error is pending.
            GL.Viewport(0, 0, -1, 1);
            owner.SetDebugName("test.owner.label");
            // Assert: managed label state changes without consuming the caller's error or selector.
            Assert.Equal("test.owner.label", owner.DebugName);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            GL.GetInteger(GetPName.ArrayBufferBinding, out int after);
            Assert.Equal(binding, after);
            GL.GetObjectLabel(ObjectLabelIdentifier.Buffer, owner.BufferId, 128, out _, out string label);
#if DEBUG
            Assert.Equal("test.owner.label", label);
#else
            Assert.Equal(string.Empty, label);
#endif
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            owner.Dispose();
            owner.SetDebugName("retired.managed.name");
            Assert.Equal("retired.managed.name", owner.DebugName);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        catch { succeeded = false; throw; }
        finally
        {
            Finish(succeeded);
        }
    }

    /// <summary>Native diagnostic failure cannot turn successful allocation or managed naming into failure.</summary>
    [Fact]
    public void LabelFailureDoesNotInvalidateOwner()
    {
        // Arrange: intercept optional native label dispatch for one live buffer.
        EnsureContextValid();
        bool succeeded = true;
        try
        {
            using var owner = RHICommandContext.Current.CreateBuffer(new RHIBufferDescription
            {
                Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 16,
                Usage = BufferUsage.StaticDraw
            }).RequireResource();
            labelAttempts = 0;
            var harmony = new Harmony("VGE.Tests.OwnerLabelFailure");
            var label = AccessTools.Method(typeof(OpenTK.Graphics.OpenGL4.GL), "ObjectLabel",
                [typeof(OpenTK.Graphics.OpenGL4.ObjectLabelIdentifier), typeof(int), typeof(int), typeof(string)]);
            try
            {
                harmony.Patch(label, prefix: new HarmonyMethod(typeof(RHIResourceLabelTests), nameof(FailLabel)));
                // Act: reject the native label call while publishing the managed name.
                owner.SetDebugName("failure.is.optional");
                // Assert: failure to label natively does not invalidate the managed owner.
                Assert.Equal("failure.is.optional", owner.DebugName);
                Assert.True(owner.IsValid);
                Assert.Equal(16, owner.SizeBytes);
#if DEBUG
                Assert.Equal(1, labelAttempts);
#else
                Assert.Equal(0, labelAttempts);
#endif
            }
            finally { harmony.UnpatchAll(harmony.Id); }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        catch { succeeded = false; throw; }
        finally
        {
            Finish(succeeded);
        }
    }

    #endregion

    #region Private
    /// <summary>Checks cleanup GL state after success and drains errors only after a failed assertion path.</summary>
    private static void Finish(bool succeeded)
    {
        if (succeeded)
            AssertNoGlErrors();
        else
            DrainGlErrors();
    }

    /// <summary>Collects and reports all errors left by a successful test and resource disposal.</summary>
    private static void AssertNoGlErrors()
    {
        var errors = new System.Collections.Generic.List<ErrorCode>();
        ErrorCode error;
        while ((error = GL.GetError()) != ErrorCode.NoError)
            errors.Add(error);
        Assert.Empty(errors);
    }

    /// <summary>Consumes errors after a failed path so they cannot mislead later tests.</summary>
    private static void DrainGlErrors()
    {
        while (GL.GetError() != ErrorCode.NoError)
        {
        }
    }

    /// <summary>Rejects only native label dispatch, leaving required resource commands untouched.</summary>
    private static void FailLabel()
    {
        labelAttempts++;
        throw new InvalidOperationException("Injected optional label failure.");
    }
    #endregion
}
