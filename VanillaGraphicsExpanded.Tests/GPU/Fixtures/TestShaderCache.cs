using System.Security.Cryptography;
using System.Text;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Tests.TestSupport;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Stores executable bytes for generated fixture kernels without sharing native owners or mutable state.</summary>
internal static class TestShaderCache
{
    internal static readonly ProgramBinaryStore Store = new("VGE.TestPrograms", fileSystem: new CountingDiskCacheFileSystem());

    #region Internal API
    #region RHI fixture construction
    /// <summary>Reuses resource-free GLSL state-fixture code while returning a new RHI owner and prepared interface.</summary>
    internal static Rendering.RHI.RHIShader CreateStateShader(IEnumerable<Rendering.RHI.RHIShaderStageDescription> descriptions)
    {
        var stages = descriptions.OrderBy(stage => stage.Stage).ToArray();
        if (stages.Any(stage => stage.Format != Rendering.RHI.ShaderCodeFormat.Glsl))
            throw new ArgumentException("State fixture stages must contain exact GLSL sources.", nameof(descriptions));
        var inputs = Prepare(string.Concat(stages.Select(stage => $"{stage.Stage}:{stage.Source!.Length}:{stage.Source}")), "state-graphics;no-transform-feedback");
        Rendering.RHI.RHIShaderBinary? binary = null;
        if (inputs.Store is not null && inputs.Store.TryRead(inputs.Key, out int format, out byte[] bytes))
        {
            if (GpuSupport.Graphics.ProgramBinaryFormats.Contains(format)) binary = new(format, bytes);
            else inputs.Store.Remove(inputs.Key);
        }
        var context = Rendering.RHI.RHICommandContext.Current;
        var result = context.CreateShader(new(Rendering.RHI.ShaderKind.Graphics, stages, new(), binary,
            retrievableBinary: inputs.Store is not null));
        if (!result.IsSuccess && binary is not null)
        {
            inputs.Store!.Remove(inputs.Key);
            binary = null;
            result = context.CreateShader(new(Rendering.RHI.ShaderKind.Graphics, stages, new(),
                retrievableBinary: true));
        }
        var shader = result.RequireResource();
        try
        {
            if (binary is null) Save(inputs, shader.Program.ProgramId);
            return shader;
        }
        catch { shader.Dispose(); throw; }
    }

    #endregion

    #region Cache identity
    /// <summary>Captures exact generated source, compiler mode and driver/profile identity under the selected suite policy.</summary>
    internal static (ProgramBinaryStore? Store, string Key) Prepare(string source, string kind = "compute;no-transform-feedback")
    {
        var (store, driver) = DriverProgramCache.CaptureStore();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"fixture-{kind};monolithic;v2\n{driver}\n{source}")));
        return (store, key);
    }

    #endregion

    #region Native fixture construction
    /// <summary>Returns a new caller-owned numerical raster executable from exact expanded sources or cached bytes.</summary>
    internal static int CreateGraphics(string vertexSource, string fragmentSource)
    {
        // Length-prefixing keeps stage boundaries unambiguous, including arbitrary source text.
        var inputs = Prepare($"{vertexSource.Length}:{vertexSource}{fragmentSource.Length}:{fragmentSource}", "graphics;no-transform-feedback");
        if (TryLoad(inputs, out int program)) return program;
        int vertex = 0, fragment = 0;
        program = GL.CreateProgram();
        try
        {
            if (inputs.Store is not null) GL.ProgramParameter(program, ProgramParameterName.ProgramBinaryRetrievableHint, 1);
            vertex = Compile(ShaderType.VertexShader, vertexSource);
            fragment = Compile(ShaderType.FragmentShader, fragmentSource);
            GL.AttachShader(program, vertex);
            GL.AttachShader(program, fragment);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            if (linked == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(program));
            GL.DetachShader(program, vertex);
            GL.DetachShader(program, fragment);
            Save(inputs, program);
            return program;
        }
        catch { GL.DeleteProgram(program); throw; }
        finally
        {
            if (vertex != 0) GL.DeleteShader(vertex);
            if (fragment != 0) GL.DeleteShader(fragment);
        }
    }

    /// <summary>Creates a fresh vertex-only color-feedback program with its fixed prelink configuration in cache identity.</summary>
    internal static int CreateColorFeedback(string vertexSource)
    {
        var inputs = Prepare(vertexSource, "vertex;transform-feedback:interleaved:color");
        if (TryLoad(inputs, out int program)) return program;
        int vertex = 0;
        program = GL.CreateProgram();
        try
        {
            if (inputs.Store is not null) GL.ProgramParameter(program, ProgramParameterName.ProgramBinaryRetrievableHint, 1);
            vertex = Compile(ShaderType.VertexShader, vertexSource);
            GL.AttachShader(program, vertex);
            GL.TransformFeedbackVaryings(program, 1, ["color"], TransformFeedbackMode.InterleavedAttribs);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            if (linked == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(program));
            GL.DetachShader(program, vertex);
            Save(inputs, program);
            return program;
        }
        catch { GL.DeleteProgram(program); throw; }
        finally { if (vertex != 0) GL.DeleteShader(vertex); }
    }

    #endregion

    #region Executable storage
    /// <summary>Creates an independently owned native executable from compatible bytes, evicting rejected binaries.</summary>
    internal static bool TryLoad((ProgramBinaryStore? Store, string Key) inputs, out int program)
    {
        program = 0;
        if (inputs.Store is null || !inputs.Store.TryRead(inputs.Key, out int format, out byte[] bytes)) return false;
        if (!GpuSupport.Graphics.ProgramBinaryFormats.Contains(format))
        {
            inputs.Store.Remove(inputs.Key);
            return false;
        }
        int candidate = GL.CreateProgram();
        try
        {
            GL.ProgramBinary(candidate, (BinaryFormat)format, bytes, bytes.Length);
            GL.GetProgram(candidate, GetProgramParameterName.LinkStatus, out int linked);
            if (linked == 0) { inputs.Store.Remove(inputs.Key); return false; }
            program = candidate;
            candidate = 0;
            return true;
        }
        finally { if (candidate != 0) GL.DeleteProgram(candidate); }
    }

    /// <summary>Saves optional executable bytes while the caller retains its original native owner.</summary>
    internal static void Save((ProgramBinaryStore? Store, string Key) inputs, int program)
    {
        if (inputs.Store is null) return;
        GL.GetProgram(program, GetProgramParameterName.ProgramBinaryLength, out int length);
        if (length <= 0 || length > ProgramBinaryStore.MaximumBinaryBytes) return;
        byte[] bytes = new byte[length];
        GL.GetProgramBinary(program, length, out int written, out BinaryFormat format, bytes);
        if (written > 0) inputs.Store.Write(inputs.Key, (int)format, bytes.AsSpan(0, written).ToArray());
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Compiles one generated stage and retires its partial allocation on failure.</summary>
    private static int Compile(ShaderType kind, string source)
    {
        int stage = GL.CreateShader(kind);
        try
        {
            GL.ShaderSource(stage, source);
            GL.CompileShader(stage);
            GL.GetShader(stage, ShaderParameter.CompileStatus, out int compiled);
            if (compiled == 0) throw new InvalidOperationException(GL.GetShaderInfoLog(stage));
            return stage;
        }
        catch { GL.DeleteShader(stage); throw; }
    }
    #endregion
}
