using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks optional owner labels without giving wrappers native diagnostic authority.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class RHIResourceLabelTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    private static int labelAttempts;

    #region Public API
    /// <summary>Labels preserve supplied names, selectors and pending errors without native object probes.</summary>
    [Fact]
    public void OwnerLabelPreservesStateAndPendingErrors()
    {
        EnsureContextValid();
        using var owner = RHICommandContext.Current.CreateBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 16,
            Usage = BufferUsage.StaticDraw
        }).RequireResource();
        GL.GetInteger(GetPName.ArrayBufferBinding, out int binding);
        // The invalid viewport creates an unrelated pending error. A label must never consume it.
        GL.Viewport(0, 0, -1, 1);
        owner.SetDebugName("test.owner.label");
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

    /// <summary>Native diagnostic failure cannot turn successful allocation or managed naming into failure.</summary>
    [Fact]
    public void LabelFailureDoesNotInvalidateOwner()
    {
        EnsureContextValid();
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
            owner.SetDebugName("failure.is.optional");
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

    /// <summary>A diagnostic entry point never asks GL to verify an already admitted allocation.</summary>
    [Fact]
    public void LabelImplementationContainsNoNativeValidityQueries()
    {
        var method = typeof(GlDebug).GetMethod(nameof(GlDebug.TryLabel))!;
        var calls = PatchProcessor.GetOriginalInstructions(method).Select(i => i.operand).OfType<MethodInfo>();
        Assert.DoesNotContain(calls, m => m.DeclaringType?.Namespace?.StartsWith("OpenTK.Graphics") == true
            && (m.Name.StartsWith("Is", StringComparison.Ordinal) || m.Name == "GetError"));
#if !DEBUG
        Assert.DoesNotContain(calls, m => m.Name == "ObjectLabel");
        var backend = typeof(OpenGLBackend).GetMethod(nameof(OpenGLBackend.LabelResource))!;
        Assert.DoesNotContain(PatchProcessor.GetOriginalInstructions(backend), i => i.operand is MethodInfo);
#endif
    }

    /// <summary>Wrapper label methods retain only managed naming and the semantic owner diagnostic boundary.</summary>
    [Fact]
    public void WrapperLabelsExposeNoNativeSelectors()
    {
        Type[] wrappers = [typeof(GpuBufferObject), typeof(GpuBufferView), typeof(GpuFramebuffer),
            typeof(GpuProgramObject), typeof(GpuProgramPipeline), typeof(GpuQuery), typeof(GpuRenderbuffer),
            typeof(GpuSampler), typeof(GpuShaderModule), typeof(GpuTexture), typeof(GpuTextureView), typeof(GpuVao)];
        foreach (var wrapper in wrappers)
        {
            var method = wrapper.GetMethod(nameof(GpuResource.SetDebugName))!;
            var calls = PatchProcessor.GetOriginalInstructions(method).Select(i => i.operand).OfType<MethodInfo>().ToArray();
            Assert.DoesNotContain(calls, m => m.DeclaringType == typeof(GlDebug)
                || m.DeclaringType?.Namespace?.StartsWith("OpenTK.Graphics") == true);
#if DEBUG
            Assert.Contains(calls, m => m.DeclaringType == typeof(RHICommandContext) && m.Name == "TryLabelResource");
#else
            Assert.DoesNotContain(calls, m => m.DeclaringType == typeof(RHICommandContext));
#endif
        }
    }
    #endregion

    #region Private
    /// <summary>Rejects only native label dispatch, leaving required resource commands untouched.</summary>
    private static void FailLabel()
    {
        labelAttempts++;
        throw new InvalidOperationException("Injected optional label failure.");
    }
    #endregion
}
