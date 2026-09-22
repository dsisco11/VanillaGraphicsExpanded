using System;
using System.Collections.Generic;
using System.Threading;

using TinyTokenizer.Ast;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Stages;

/// <summary>Loads stage source for diagnostics while the owning program loads built shader binaries.</summary>
internal sealed class Shader
{
    private readonly string stageExtension;
    private readonly EnumShaderType engineShaderType;

    private readonly Func<IShader?> getSlot;
    private readonly Action<IShader> setSlot;

    public ShaderSourceCode? SourceCode { get; private set; }

    public SyntaxTree? ParsedTree => SourceCode?.ParsedTree;

    public SyntaxTree? FinalTree => SourceCode?.FinalTree;

    public string? EmittedSource => SourceCode?.EmittedSource;

    #region Stage source lifecycle
    /// <summary>Connects a source stage to the owning program's engine shader slot.</summary>
    public Shader(string stageExtension, EnumShaderType engineShaderType, Func<IShader?> getSlot, Action<IShader> setSlot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageExtension);
        this.stageExtension = stageExtension;
        this.engineShaderType = engineShaderType;
        this.getSlot = getSlot ?? throw new ArgumentNullException(nameof(getSlot));
        this.setSlot = setSlot ?? throw new ArgumentNullException(nameof(setSlot));
    }

    /// <summary>Processes source for diagnostics and maintains the engine stage slot without compiling GLSL.</summary>
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

    #endregion
}
