using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies native callback interruption without retries or false certification of affected bindings.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class BindingPublicationInterruptionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>A callback before or after the driver write leaves only its affected selector uncertain until the next command.</summary>
    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    [InlineData(3, false)] [InlineData(3, true)]
    public unsafe void OverlappingCallbackDoesNotCertifyOrRetryPublication(int kind, bool after)
    {
        // Arrange
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            var cache = OpenGlStateCache.Current;
            using var texture = context.CreateTexture2D(new() { Dimension = TextureDimension.Two,
                Format = TextureFormat.Rgba8, Width = 1, Height = 1, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
            using var alternate = context.CreateTexture2D(new() { Dimension = TextureDimension.Two,
                Format = TextureFormat.Rgba8, Width = 1, Height = 1, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
            using var sampler = context.CreateSampler(new()).RequireResource();
            using var buffer = context.CreateUniformBuffer(new() { Role = BufferRole.Uniform,
                Mode = ResourceStorageMode.Mutable, Capacity = 64, Usage = BufferUsage.DynamicDraw }).RequireResource();
            RHISampledTextureBindings sampled = [new(0, texture, kind == 1 ? sampler : default)];
            int calls = 0;
            bool injected = false;
            using var native = new NativeDriverCapture();
            /// <summary>Changes the same selector through a distinct, observed native entry point exactly once.</summary>
            void Mutate()
            {
                if (injected) return;
                injected = true;
                native.Observe(() =>
                {
                    switch (kind)
                    {
                        case 0: GL.BindTextureUnit(0, alternate.TextureId); break;
                        case 1: GL.BindSampler(0, 0); break;
                        case 2: GL.BindImageTextures(0, 1, new[] { 0 }); break;
                        case 3: GL.BindBufferRange(BufferRangeTarget.UniformBuffer, 0, buffer.BufferId, 0, 16); break;
                    }
                });
            }
            if (kind is 0 or 1)
            {
                string name = kind == 0 ? "glBindTextures" : "glBindSamplers";
                nint original = native.Original(name);
                native.Add(name, new MultiBind((first, count, names) =>
                {
                    calls++;
                    if (!after) Mutate();
                    ((delegate* unmanaged[Stdcall]<uint, int, uint*, void>)original)(first, count, names);
                    if (after) Mutate();
                }));
            }
            else if (kind == 2)
            {
                nint original = native.Original("glBindImageTexture");
                native.Add("glBindImageTexture", new BindImage((unit, name, level, layered, layer, access, format) =>
                {
                    calls++;
                    if (!after) Mutate();
                    ((delegate* unmanaged[Stdcall]<uint, uint, int, byte, int, int, int, void>)original)(unit, name, level, layered, layer, access, format);
                    if (after) Mutate();
                }));
            }
            else
            {
                nint original = native.Original("glBindBufferBase");
                native.Add("glBindBufferBase", new BindBase((target, slot, name) =>
                {
                    calls++;
                    if (!after) Mutate();
                    ((delegate* unmanaged[Stdcall]<int, uint, uint, void>)original)(target, slot, name);
                    if (after) Mutate();
                }));
            }
            native.Install();
            cache.Invalidate(EPipelineState.TextureBindings | EPipelineState.SamplerBindings | EPipelineState.ImageBindings | EPipelineState.BufferBindings);
            /// <summary>Publishes through the normal family executor without a synthetic shader owner.</summary>
            void Publish()
            {
                if (kind is 0 or 1) context.BindSampledTextures(sampled);
                else if (kind == 2) OpenGLImageSlotBindings.BindImageTexture(cache, 0, new RHIImageBinding(texture));
                else context.BindBufferBase(BufferRole.Uniform, 0, buffer);
            }
            // Act
            Assert.Throws<InvalidOperationException>(Publish);

            // Assert
            native.RequireSuccessfulObservation();
            Assert.Equal(1, calls);

            // Act: retry the affected publication; the next identical attempt should be suppressed.
            Publish();

            // Assert
            Assert.Equal(2, calls);

            // Act
            Publish();

            // Assert
            Assert.Equal(2, calls);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Program and active-unit callbacks cannot redirect later work while the outer cache claims its intended selection.</summary>
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public unsafe void SelectorCallbackCannotCertifyOuterSelection(bool program, bool after)
    {
        // Arrange
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            var cache = OpenGlStateCache.Current;
            using var desired = context.CreateShader(RHIShaderInputs.EmptyComputeDescription()).RequireResource();
            using var alternate = context.CreateShader(RHIShaderInputs.EmptyComputeDescription()).RequireResource();
            int calls = 0;
            bool injected = false;
            using var native = new NativeDriverCapture();
            string name = program ? "glUseProgram" : "glActiveTexture";
            nint original = native.Original(name);
            /// <summary>Issues one distinct external command; its nested callback performs no further mutation.</summary>
            void Mutate()
            {
                if (injected) return;
                injected = true;
                native.Observe(() =>
                {
                    if (program) GL.UseProgram(alternate.Program.ProgramId);
                    else GL.ActiveTexture(TextureUnit.Texture1);
                });
            }
            native.Add(name, new SelectNative(value =>
            {
                calls++;
                if (!after) Mutate();
                ((delegate* unmanaged[Stdcall]<int, void>)original)(value);
                if (after) Mutate();
            }));
            native.Install();
            cache.Invalidate(program ? EPipelineState.Program : EPipelineState.ActiveTextureUnit);
            /// <summary>Uses the real backend selector and its executed cache.</summary>
            void Select()
            {
                if (program) OpenGLProgramSelection.Select(cache, desired.Program.ProgramId);
                else OpenGLImageBindings.ActiveTexture(cache, 0);
            }
            // Act
            Assert.Throws<InvalidOperationException>(Select);

            // Assert
            native.RequireSuccessfulObservation();
            Assert.Equal(2, calls);

            // Act: repeat the selector and confirm the now-certified selection is suppressed.
            Select();

            // Assert
            Assert.Equal(3, calls);

            // Act
            Select();

            // Assert
            Assert.Equal(3, calls);
            Assert.Equal(program ? desired.Program.ProgramId : (int)TextureUnit.Texture0,
                GL.GetInteger(program ? GetPName.CurrentProgram : GetPName.ActiveTexture));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>A selector callback during a bound clear cannot make the outer write certify the wrong texture unit.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void ActiveUnitCallbackInterruptsBoundTextureClear(bool after)
    {
        // Arrange
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            var cache = OpenGlStateCache.Current;
            using var two = context.CreateTexture2D(new() { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8,
                Width = 1, Height = 1, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
            using var three = context.CreateTexture3D(new() { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba8,
                Width = 1, Height = 1, Depth = 1, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
            context.BindSampledTextures([new(0, three, Dimension: TextureDimension.Three)]);
            context.BindSampledTextures([new(0, two), new(1, two)]);
            GL.ActiveTexture(TextureUnit.Texture0);
            RHISampledTextureBindings clear = [new(0, default)];
            int calls = 0;
            bool injected = false;
            using var native = new NativeDriverCapture();
            nint original = native.Original("glBindTexture");
            /// <summary>Redirects the active selector once through the ordinary observed native entry point.</summary>
            void Mutate()
            {
                if (injected) return;
                injected = true;
                native.Observe(() => GL.ActiveTexture(TextureUnit.Texture1));
            }
            native.Add("glBindTexture", new BindTexture((target, texture) =>
            {
                calls++;
                if (!after) Mutate();
                ((delegate* unmanaged[Stdcall]<int, uint, void>)original)(target, texture);
                if (after) Mutate();
            }));
            native.Install();
            // Act
            Assert.Throws<InvalidOperationException>(() => context.BindSampledTextures(clear));

            // Assert
            native.RequireSuccessfulObservation();
            Assert.Equal(1, calls);
            Assert.False(cache.TryGetCachedBoundTexture(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, 0, out _));
            Assert.False(cache.TryGetCachedBoundTexture(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, 1, out _));
            Assert.True(cache.TryGetCachedBoundTexture(OpenTK.Graphics.OpenGL.TextureTarget.Texture3D, 0, out int retained));
            Assert.Equal(three.TextureId, retained);

            // Act: retry the clear after restoring the selector, then repeat it to check suppression.
            context.BindSampledTextures(clear);

            // Assert
            Assert.Equal(2, calls);

            // Act
            context.BindSampledTextures(clear);

            // Assert
            Assert.Equal(2, calls);
            Assert.Equal(0, GL.GetInteger(GetPName.TextureBinding2D));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Matches target-specific texture binding ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void BindTexture(int target, uint texture);
    /// <summary>Matches the single 32-bit executable or active-texture selector ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void SelectNative(int value);
    /// <summary>Matches native texture and sampler multi-bind ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate void MultiBind(uint first, int count, uint* names);
    /// <summary>Matches native image-view binding ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void BindImage(uint unit, uint name, int level, byte layered, int layer, int access, int format);
    /// <summary>Matches native whole indexed-buffer binding ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void BindBase(int target, uint slot, uint name);
    #endregion
}
