using System;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks change-driven geometry and pass preparation independently of requested native rendering effects.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GraphicsPreparationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API


    /// <summary>Repeated pass requests reuse structural facts while executing each clear and refreshing after attachment resize.</summary>
    [Fact]
    public unsafe void PassReuseNeverSuppressesRequestedClears()
    {
        // Arrange: Describe one clear pass and observe native framebuffer allocations while preparation stays lazy.
        EnsureContextValid();
        var commands = RHICommandContext.Current;
        using var texture = commands.CreateDynamicTexture2D(new() { Dimension = TextureDimension.Two, Width = 1, Height = 1,
            Format = TextureFormat.Rgba8, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
        var pass = new RHIRenderPassDescription([new(Image: RHIAttachment.FromTexture(texture), Load: AttachmentLoad.Clear,
            Clear: ColorClearValue.Float(1, 0, 0, 1))]);
        var backend = Backend(commands);
        int allocations = 0;
        using var native = new NativeDriverCapture();
        nint create = native.Original("glCreateFramebuffers");
        native.Add("glCreateFramebuffers", new CreateFramebuffers((count, names) =>
        {
            ((delegate* unmanaged[Stdcall]<int, uint*, void>)create)(count, names);
            native.Observe(() => allocations += count);
        }));
        native.Install();
        // Act: Prepare without execution, execute the clear twice, then resize the attachment and execute again.
        commands.DescribeRenderTargets(pass);
        Assert.Equal(0, allocations);
        void Execute()
        {
            using var boundary = commands.EnterBoundary(new("Tests.PreparedClear", true, operations: BoundaryOperations.RenderPasses));
            boundary.Run(() => { commands.BeginRenderPass(pass); commands.EndRenderPass(); });
        }
        Execute();
        Assert.Equal(1, allocations);
#if DEBUG
        long prepared = backend.PassPreparations;
#endif
        commands.ClearTexture(texture, new(new(), 0, 0, 0, 1, 1), new(PixelChannelLayout.Rgba, PixelComponentType.UnsignedByte), new byte[] { 0, 255, 0, 255 });
        Execute();
        Assert.Equal(1, allocations);
#if DEBUG
        Assert.Equal(prepared, backend.PassPreparations);
#endif
        // Assert: Reuse avoids another allocation/preparation, requested clears still execute, and resize refreshes preparation.
        Assert.Equal(1, texture.ReadTexel()[0]);
        Assert.True(texture.Resize(2, 1));
        Execute();
#if DEBUG
        Assert.Equal(prepared + 1, backend.PassPreparations);
#endif
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        native.RequireSuccessfulObservation();
    }
    #endregion

    #region Private
    /// <summary>Matches named framebuffer creation without changing its arguments or outputs.</summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private unsafe delegate void CreateFramebuffers(int count, uint* names);
    /// <summary>Uses an inline executable with one active attribute and no resource bindings.</summary>
    private static RHIShader CreateShader() => RHICommandContext.Current.CreateShader(new(ShaderKind.Graphics,
        [new(ShaderStage.Vertex, "#version 450\nlayout(location=0) in vec2 position; void main(){gl_Position=vec4(position,0,1);}"),
         new(ShaderStage.Fragment, "#version 450\nlayout(location=0) out vec4 color; void main(){color=vec4(1);}")], new())).RequireResource();
    /// <summary>Inspects the existing native owner without adding a production diagnostics API.</summary>
    private static OpenGLBackend Backend(RHICommandContext context) => (OpenGLBackend)typeof(RHICommandContext)
        .GetField("backend", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
    #endregion
}
