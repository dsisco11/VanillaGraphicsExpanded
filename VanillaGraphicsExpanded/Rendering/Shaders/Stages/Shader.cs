using System;
using System.Collections.Generic;
using System.Threading;

using OpenTK.Graphics.OpenGL;

using TinyTokenizer.Ast;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Stages;

internal sealed class Shader
{
    internal sealed class StageCompileDiagnostics
    {
        public required bool Success { get; init; }
        public required string InfoLog { get; init; }
        public required int ShaderId { get; init; }
    }

    private readonly string stageExtension;
    private readonly EnumShaderType engineShaderType;

    private readonly Func<IShader?> getSlot;
    private readonly Action<IShader> setSlot;

    public ShaderSourceCode? SourceCode { get; private set; }

    public SyntaxTree? ParsedTree => SourceCode?.ParsedTree;

    public SyntaxTree? FinalTree => SourceCode?.FinalTree;

    public string? EmittedSource => SourceCode?.EmittedSource;

    public Shader(string stageExtension, EnumShaderType engineShaderType, Func<IShader?> getSlot, Action<IShader> setSlot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageExtension);
        this.stageExtension = stageExtension;
        this.engineShaderType = engineShaderType;
        this.getSlot = getSlot ?? throw new ArgumentNullException(nameof(getSlot));
        this.setSlot = setSlot ?? throw new ArgumentNullException(nameof(setSlot));
    }

    public ShaderSourceCode LoadAndApply(
        ICoreClientAPI api,
        string shaderName,
        IReadOnlyDictionary<string, string?> defines,
        ILogger? log = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderName);

        var engineShader = getSlot();
        if (engineShader is null)
        {
            engineShader = api.Shader.NewShader(engineShaderType);
            setSlot(engineShader);
        }

        SourceCode = ShaderSourceCode.Load(api, shaderName, stageExtension, defines, log, ct);
        engineShader.Code = SourceCode.EmittedSource;

        return SourceCode;
    }

    public bool TryLoadAndApplyOptional(
        ICoreClientAPI api,
        string shaderName,
        IReadOnlyDictionary<string, string?> defines,
        ILogger? log = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderName);

        string domain = SourceCode?.AssetDomain ?? VanillaGraphicsExpanded.PBR.ShaderImportsSystem.DefaultDomain;
        string stageSourceName = $"{shaderName}.{stageExtension}";
        string assetPath = $"shaders/{stageSourceName}";

        IAsset? asset = api.Assets.TryGet(AssetLocation.Create(assetPath, domain), loadAsset: true);
        if (asset is null)
        {
            return false;
        }

        LoadAndApply(api, shaderName, defines, log, ct);
        return true;
    }

    public StageCompileDiagnostics CompileDiagnostics()
    {
        if (string.IsNullOrEmpty(EmittedSource))
        {
            return new StageCompileDiagnostics { Success = false, InfoLog = "[VGE] No emitted source available for diagnostics", ShaderId = 0 };
        }

        int shader = 0;
        try
        {
            ShaderType type = MapToOpenTk(engineShaderType);

            shader = GL.CreateShader(type);
            GL.ShaderSource(shader, EmittedSource);
            GL.CompileShader(shader);

            GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
            string log = GL.GetShaderInfoLog(shader) ?? string.Empty;

            return new StageCompileDiagnostics
            {
                Success = status != 0,
                InfoLog = log,
                ShaderId = shader
            };
        }
        catch (Exception ex)
        {
            if (shader != 0)
            {
                try { GL.DeleteShader(shader); } catch { /* ignore */ }
            }

            return new StageCompileDiagnostics
            {
                Success = false,
                InfoLog = $"[VGE] Exception while compiling shader diagnostics: {ex}",
                ShaderId = 0
            };
        }
    }

    public static void DeleteDiagnosticsShader(in StageCompileDiagnostics diag)
    {
        if (diag.ShaderId != 0)
        {
            try { GL.DeleteShader(diag.ShaderId); } catch { /* ignore */ }
        }
    }

    private static ShaderType MapToOpenTk(EnumShaderType type)
    {
        return type switch
        {
            EnumShaderType.VertexShader => ShaderType.VertexShader,
            EnumShaderType.FragmentShader => ShaderType.FragmentShader,
            EnumShaderType.GeometryShader => ShaderType.GeometryShader,
            EnumShaderType.ComputeShader => ShaderType.ComputeShader,
            EnumShaderType.TessControlShader => ShaderType.TessControlShader,
            EnumShaderType.TessEvaluationShader => ShaderType.TessEvaluationShader,
            _ => throw new NotSupportedException($"Unsupported shader stage type for diagnostics: {type}")
        };
    }
}
