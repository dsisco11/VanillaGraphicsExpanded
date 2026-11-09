using ShaderBuildTool.Spirv;
using Vintagestory.API.Common;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;

namespace VanillaGraphicsExpanded.Tests.Support.Lighting;

/// <summary>Dispatches a minimal packaged compute write into one admitted surface-work collector slot.</summary>
internal sealed class SurfaceWorkCollectorComputeProgram : GpuComputeProgram
{
    private static readonly GpuShaderContract contract=TestShaderPrograms.Create().Programs["tests/surface-work-collector"];

    #region Public API
    /// <summary>Creates the probe executable for the current test API and diagnostic collector.</summary>
    internal static SurfaceWorkCollectorComputeProgram Create(ICoreAPI api,SurfaceWorkDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(api);ArgumentNullException.ThrowIfNull(diagnostics);
        var layout=new GpuProgramLayout();layout.RegisterContract(contract.Stages[0].Bindings);
        if(!ComputeShaderAsset.TryCreateFromAssets(api,new ShaderSettings(contract),out var asset,out string log,
            debugName:"Tests.SurfaceWorkCollector",layout:layout))
            throw new InvalidOperationException("Surface-work collector compute shader unavailable: "+log);
        return new(asset!,diagnostics);
    }
    #endregion

    #region Protected API
    /// <summary>Publishes the currently admitted slot as the compute shader's binding-four counter block.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
    {
        var buffer=diagnostics.ActiveBuffer??throw new InvalidOperationException("The collector has not admitted a diagnostic sample.");
        return new(Shader,buffers:[new(GpuBindingEntry.Identity(ShaderBindingKind.StorageBlock,"DiagnosticCounters"),BufferRole.ShaderStorage,buffer)]);
    }
    #endregion

    #region Private
    private readonly SurfaceWorkDiagnostics diagnostics;

    /// <summary>Retains the prepared fixture executable and borrows its collector only while dispatching.</summary>
    private SurfaceWorkCollectorComputeProgram(ComputeShaderAsset asset,SurfaceWorkDiagnostics diagnostics):base(asset)
        =>this.diagnostics=diagnostics;
    #endregion
}
