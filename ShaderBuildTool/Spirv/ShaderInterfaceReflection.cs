using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Silk.NET.SPIRV;
using Silk.NET.SPIRV.Cross;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Spirv;

/// <summary>Copies portable interface facts from compiler-owned reflection without decoding instructions.</summary>
internal sealed unsafe class ShaderInterfaceReflection : IDisposable
{
    private readonly Cross api = Cross.GetApi();
    private Context* context;
    private Compiler* compiler;

    #region Public API
    /// <summary>Parses an inspection binary and selects the exact declared entry point.</summary>
    internal ShaderInterfaceReflection(byte[] binary, ShaderStageContract stage)
    {
        try
        {
            Check(api.ContextCreate(ref context));
            if (binary.Length == 0 || binary.Length % sizeof(uint) != 0)
                throw new InvalidDataException("Interface reflection requires a word-aligned shader binary.");
            fixed (byte* bytes = binary)
            {
                ParsedIr* ir = null;
                Check(api.ContextParseSpirv(context, (uint*)bytes, (nuint)(binary.Length / sizeof(uint)), ref ir));
                Check(api.ContextCreateCompiler(context, Backend.None, ir, CaptureMode.TakeOwnership, ref compiler));
            }
            Check(api.CompilerSetEntryPoint(compiler, stage.EntryPoint, Model(stage.Kind)));
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Extracts numeric declarations while leaving final linked activity to the driver.</summary>
    internal PackagedStageInterface Read()
    {
        Resources* resources = null;
        Check(api.CompilerCreateShaderResources(compiler, ref resources));
        ExecutionMode* modes = null; nuint modeCount = 0;
        Check(api.CompilerGetExecutionModes(compiler, ref modes, ref modeCount));
        var execution = ImmutableArray.CreateBuilder<PackagedExecutionMode>();
        for (nuint i = 0; i < modeCount; i++)
        {
            // Query only operands the compiler exposes. Single-operand queries ignore their
            // index, and unsupported operand-bearing modes otherwise silently return zero.
            int arguments = ArgumentCount(modes[i]);
            execution.Add(new((uint)modes[i], [
                arguments > 0 ? api.CompilerGetExecutionModeArgumentByIndex(compiler, modes[i], 0) : 0,
                arguments > 1 ? api.CompilerGetExecutionModeArgumentByIndex(compiler, modes[i], 1) : 0,
                arguments > 2 ? api.CompilerGetExecutionModeArgumentByIndex(compiler, modes[i], 2) : 0],
                modes[i] == ExecutionMode.LocalSizeId));
        }
        return new(Variables(resources, ResourceType.StageInput),
            Variables(resources, ResourceType.StageOutput), execution.ToImmutable());
    }

    /// <summary>Releases all reflection objects through their owning compiler context.</summary>
    public void Dispose()
    {
        if (context != null) { api.ContextDestroy(context); context = null; compiler = null; }
        api.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Bounds metadata to execution modes whose operands are available through compiler reflection.</summary>
    private static int ArgumentCount(ExecutionMode mode) => mode switch
    {
        ExecutionMode.LocalSize or ExecutionMode.LocalSizeId => 3,
        ExecutionMode.Invocations or ExecutionMode.OutputVertices => 1,
        ExecutionMode.SpacingEqual or ExecutionMode.SpacingFractionalEven or ExecutionMode.SpacingFractionalOdd or
        ExecutionMode.VertexOrderCw or ExecutionMode.VertexOrderCcw or ExecutionMode.PixelCenterInteger or
        ExecutionMode.OriginUpperLeft or ExecutionMode.OriginLowerLeft or ExecutionMode.EarlyFragmentTests or
        ExecutionMode.PointMode or ExecutionMode.Xfb or ExecutionMode.DepthReplacing or ExecutionMode.DepthGreater or
        ExecutionMode.DepthLess or ExecutionMode.DepthUnchanged or ExecutionMode.InputPoints or ExecutionMode.InputLines or
        ExecutionMode.InputLinesAdjacency or ExecutionMode.Triangles or ExecutionMode.InputTrianglesAdjacency or
        ExecutionMode.Quads or ExecutionMode.Isolines or ExecutionMode.OutputPoints or ExecutionMode.OutputLineStrip or
        ExecutionMode.OutputTriangleStrip => 0,
        _ => throw new InvalidDataException($"Unsupported shader execution mode: {mode}.")
    };

    /// <summary>Copies numeric interface declarations; interface blocks require explicit member handling.</summary>
    private ImmutableArray<PackagedInterfaceVariable> Variables(Resources* resources, ResourceType kind)
    {
        ReflectedResource* variables = null; nuint count = 0;
        Check(api.ResourcesGetResourceListForType(resources, kind, ref variables, ref count));
        var result = ImmutableArray.CreateBuilder<PackagedInterfaceVariable>();
        for (nuint i = 0; i < count; i++)
        {
            var variable = variables[i];
            var type = api.CompilerGetTypeHandle(compiler, variable.TypeId);
            if (api.TypeGetNumMemberTypes(type) != 0)
                throw new InvalidDataException("Stage interface blocks require member-location reflection.");
            if (api.CompilerHasDecoration(compiler, variable.Id, Decoration.Location) == 0)
                throw new InvalidDataException("User stage interface has no explicit location.");
            var scalar = api.TypeGetBasetype(type) switch
            {
                Basetype.Boolean => ShaderScalarType.Bool,
                Basetype.Int32 => ShaderScalarType.Int,
                Basetype.Uint32 => ShaderScalarType.UInt,
                Basetype.FP32 or Basetype.FP64 => ShaderScalarType.Float,
                _ => throw new InvalidDataException("Unsupported stage interface scalar type.")
            };
            result.Add(new(api.CompilerGetDecoration(compiler, variable.Id, Decoration.Location),
                api.CompilerGetDecoration(compiler, variable.Id, Decoration.Index),
                api.CompilerGetDecoration(compiler, variable.Id, Decoration.Component), scalar,
                api.TypeGetBitWidth(type), api.TypeGetVectorSize(type), api.TypeGetColumns(type), Dimensions(type)));
        }
        return result.ToImmutable();
    }

    /// <summary>Requires concrete compiler-resolved extents rather than interpreting specialization expressions.</summary>
    private ImmutableArray<uint> Dimensions(CrossType* type)
    {
        var dimensions = ImmutableArray.CreateBuilder<uint>();
        for (uint i = 0; i < api.TypeGetNumArrayDimensions(type); i++)
        {
            if (api.TypeArrayDimensionIsLiteral(type, i) == 0)
                throw new InvalidDataException("Stage interface array extent is not resolved for this selection.");
            dimensions.Add(api.TypeGetArrayDimension(type, i));
        }
        return dimensions.ToImmutable();
    }

    /// <summary>Reports the compiler's diagnostic while the owning context is still alive.</summary>
    private void Check(Result result)
    {
        if (result != Result.Success)
            throw new InvalidDataException("Shader interface reflection failed: " +
                (context == null ? result.ToString() : Marshal.PtrToStringUTF8((nint)api.ContextGetLastErrorString(context))));
    }

    /// <summary>Maps the existing stage contract to the compiler's execution-model enum.</summary>
    private static ExecutionModel Model(ShaderStageKind stage) => stage switch
    {
        ShaderStageKind.Vertex => ExecutionModel.Vertex,
        ShaderStageKind.Fragment => ExecutionModel.Fragment,
        ShaderStageKind.Geometry => ExecutionModel.Geometry,
        ShaderStageKind.TessellationControl => ExecutionModel.TessellationControl,
        ShaderStageKind.TessellationEvaluation => ExecutionModel.TessellationEvaluation,
        ShaderStageKind.Compute => ExecutionModel.GLCompute,
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };
    #endregion
}
