using System.Runtime.InteropServices;
using System.Text;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies native factory aliases and combined compiler outputs through both generated binding roots.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed unsafe class GpuExecutableFactoryTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>ARB alias outputs establish stage and fresh-program defaults without a native descriptor probe.</summary>
    [Fact]
    public void ArbFactoryAliasesMaintainReturnedObjects()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int shader = 0, program = 0;
        try
        {
            using var driver = new NativeDriverCapture();
            ConfigureOptionalAliases(driver);
            driver.Install();
            var table = OpenGLBindingTable.For(typeof(GL));
            var stageFactory = (delegate* unmanaged[Stdcall]<int, int>)table.Read("glCreateShaderObjectARB");
            var programFactory = (delegate* unmanaged[Stdcall]<int>)table.Read("glCreateProgramObjectARB");
            shader = stageFactory((int)ShaderType.FragmentShader);
            program = programFactory();
            Assert.NotEqual(0, shader); Assert.NotEqual(0, program);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var stage = Assert.IsType<GpuResourceMetadataEntry<GpuShaderMetadata>>(manager.FindResource(GpuResourceKind.Shader, shader));
            var executable = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(manager.FindResource(GpuResourceKind.Program, program));
            Assert.True(stage.IsUsable); Assert.Equal(ShaderStage.Fragment, stage.Metadata.Type);
            Assert.True(executable.IsUsable); Assert.False(executable.Metadata.ExecutableAvailable);
            Assert.Empty(executable.Metadata.Resources); Assert.Empty(executable.Metadata.TextureUnits);
            Assert.Equal(1UL, stage.MetadataRevision); Assert.Equal(1UL, executable.MetadataRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { if (program != 0) GL.DeleteProgram(program); if (shader != 0) GL.DeleteShader(shader); fixture.CleanupGpuResources(); }
    }

    /// <summary>Combined factories retain successful, failed and zero-source returned program identities.</summary>
    [Theory]
    [InlineData(false, false, 0)] [InlineData(true, false, 0)]
    [InlineData(false, false, 1)] [InlineData(true, false, 1)]
    [InlineData(false, false, 2)] [InlineData(true, false, 2)]
    [InlineData(false, true, 0)] [InlineData(true, true, 0)]
    [InlineData(false, true, 1)] [InlineData(true, true, 1)]
    public void CombinedFactoriesCompleteOnlyDriverProducedExecutables(bool core, bool ext, int variant)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int program = 0;
        try
        {
            using var driver = new NativeDriverCapture();
            ConfigureOptionalAliases(driver);
            driver.Install();
            var table = OpenGLBindingTable.For(core ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));
            byte[] text = Encoding.UTF8.GetBytes((variant == 0
                ? "#version 450\nlayout(location=0) out vec4 color; void main(){color=vec4(1);}" : "invalid shader") + '\0');
            fixed (byte* bytes = text)
            {
                nint source = (nint)bytes;
                if (ext)
                    program = ((delegate* unmanaged[Stdcall]<int, nint, int>)table.Read("glCreateShaderProgramEXT"))((int)ShaderType.FragmentShader, source);
                else
                    program = ((delegate* unmanaged[Stdcall]<int, int, nint, int>)table.Read("glCreateShaderProgramv"))(
                        (int)ShaderType.FragmentShader, variant == 2 ? 0 : 1, variant == 2 ? 0 : (nint)(&source));
            }
            Assert.NotEqual(0, program);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var original = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(manager.FindResource(GpuResourceKind.Program, program));
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            if (variant != 2) Assert.Equal(variant == 0, linked != 0);
            Assert.Same(original, manager.FindResource(GpuResourceKind.Program, program));
            Assert.True(original.IsRealized); Assert.True(original.IsUsable);
            Assert.Equal(linked != 0, original.Metadata.ExecutableAvailable);
            if (linked == 0) { Assert.Empty(original.Metadata.Resources); Assert.Empty(original.Metadata.TextureUnits); }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { if (program != 0) GL.DeleteProgram(program); fixture.CleanupGpuResources(); }
    }
    #endregion

    #region Private
    /// <summary>Provides deterministic optional alias delivery backed by real core factories, without claiming optional driver execution.</summary>
    private static void ConfigureOptionalAliases(NativeDriverCapture driver)
    {
        nint shader = driver.Original("glCreateShader"), program = driver.Original("glCreateProgram"), combined = driver.Original("glCreateShaderProgramv");
        driver.Add("glCreateShaderObjectARB", new StageFactory(stage => ((delegate* unmanaged[Stdcall]<int, int>)shader)(stage)));
        driver.Add("glCreateProgramObjectARB", new ProgramFactory(() => ((delegate* unmanaged[Stdcall]<int>)program)()));
        driver.Add("glCreateShaderProgramEXT", new CombinedFactory((stage, source) =>
            ((delegate* unmanaged[Stdcall]<int, int, nint, int>)combined)(stage, 1, (nint)(&source))));
    }
    /// <summary>Matches a stage-specific native factory result.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int StageFactory(int stage);
    /// <summary>Matches the native empty-program factory result.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ProgramFactory();
    /// <summary>Matches the desktop EXT single-source combined factory ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CombinedFactory(int stage, nint source);
    #endregion
}
