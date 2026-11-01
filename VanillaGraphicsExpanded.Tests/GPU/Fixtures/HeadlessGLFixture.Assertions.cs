using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Retains ordinary production checker shader owners until their physical fixture context retires.</summary>
public sealed partial class HeadlessGLFixture
{
    [ThreadStatic] private static HeadlessGLFixture? currentFixture;
    private readonly GpuAssertionProgram?[,] assertionPrograms = new GpuAssertionProgram?[3, 4];
    private readonly GpuAssertionProgram?[] projectionPrograms = new GpuAssertionProgram?[3];
    private GpuAssertionProgram? layerProgram;
    private GpuAssertionProgram? predicateProgram;
    private (nint Handle, long Generation) assertionContext;
    private bool preparingAssertions;
    private bool retiringContext;
    private RHIResourceManager? assertionManager;

    #region Internal API
    /// <summary>Obtains the already selected fixture, rejecting absent or foreign context authority.</summary>
    internal static HeadlessGLFixture Current
    {
        get
        {
            var fixture = currentFixture ?? throw new InvalidOperationException("Select the GPU fixture before observing an image.");
            ObjectDisposedException.ThrowIf(fixture.retiringContext, fixture);
            if (fixture.assertionContext != RenderContextRegistry.Current())
                throw new InvalidOperationException("The selected fixture does not own the current physical context.");
            return fixture;
        }
    }

    /// <summary>Borrows the existing fixture-owned production checker for its numeric interpretation.</summary>
    internal GpuAssertionProgram AssertionProgram(int kind, int expression)
    {
        var program = assertionPrograms[kind, expression] ?? throw new InvalidOperationException("Checker assets are not prepared.");
        program.RequireContext(); return program;
    }

    /// <summary>Borrows the existing fixture-owned production projection for its numeric interpretation.</summary>
    internal GpuAssertionProgram ProjectionProgram(int kind)
    {
        var program = projectionPrograms[kind] ?? throw new InvalidOperationException("Projection assets are not prepared.");
        program.RequireContext(); return program;
    }

    /// <summary>Borrows the fixed fixture-owned format-conversion program.</summary>
    internal GpuAssertionProgram LayerProgram => layerProgram ?? throw new InvalidOperationException("Layer assets are not prepared.");

    /// <summary>Borrows the authored-predicate executable independently of generic statistics preparation.</summary>
    internal GpuAssertionProgram PredicateProgram => predicateProgram ?? throw new InvalidOperationException("Predicate assets are not prepared.");
    #endregion

    #region Private
    /// <summary>Prepares declared shader assets outside render boundaries and before hostile assertion selectors.</summary>
    private void PrepareAssertionPrograms()
    {
        currentFixture = this;
        assertionContext = RenderContextRegistry.Current();
        if (layerProgram != null && assertionManager?.IsMetadataRegistryRetired == true)
        {
            // Explicit service-lifecycle subjects can retire the manager while retaining the GL context.
            // Its old registrations cannot certify a new execution; retire original owners before loading again.
            DisposeAssertionPrograms();
        }
        if (layerProgram != null || preparingAssertions || retiringContext) return;
        preparingAssertions = true;
        try
        {
            assertionManager = RHIResourceManagerSystem.EnsureCurrentContext();
            var registry = ShaderBuildTool.Spirv.TestShaderPrograms.Create();
            /// <summary>Transfers one complete production asset to its typed compute input owner.</summary>
            GpuAssertionProgram Load(string family, int kind, int expression)
            {
                var settings = new ShaderSettings(registry.Programs[$"tests/assertion-{family}-{kind}-{expression}"]);
                var selected = new ShaderLoadPlan(settings).Stages.Single();
                string binary = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", selected.BinaryPath);
                if (!ComputeShaderAsset.TryLoadFromSpirv(binary, settings, out var asset, out string log,
                    fileSystem: ShaderAssetFileSystem.Built))
                    throw new InvalidOperationException("Could not prepare checker asset: " + log);
                try { return new GpuAssertionProgram(asset!, family == "predicate" ? "check" : family); }
                catch (Exception error)
                {
                    try { asset!.Dispose(); }
                    catch (Exception cleanup) { error.Data["CheckerAssetCleanup"] = cleanup; }
                    throw;
                }
            }
            for (int kind = 0; kind < 3; kind++)
            {
                for (int expression = 0; expression < 4; expression++) assertionPrograms[kind, expression] = Load("check", kind, expression);
                projectionPrograms[kind] = Load("project", kind, 0);
            }
            predicateProgram = Load("predicate", 0, 0);
            layerProgram = Load("layer", 0, 0);
        }
        catch (Exception error)
        {
            try { DisposeAssertionPrograms(); }
            catch (Exception cleanup) { error.Data["CheckerOwnerCleanup"] = cleanup; }
            throw;
        }
        finally { preparingAssertions = false; }
    }

    /// <summary>Retires production shader owners before their originating manager/context.</summary>
    private void DisposeAssertionPrograms()
    {
        List<Exception>? errors = null;
        // Cleanup attempts every original owner even if one retirement reports a failure.
        foreach (var program in assertionPrograms)
            try { program?.Dispose(); } catch (Exception error) { (errors ??= []).Add(error); }
        Array.Clear(assertionPrograms);
        foreach (var program in projectionPrograms)
            try { program?.Dispose(); } catch (Exception error) { (errors ??= []).Add(error); }
        Array.Clear(projectionPrograms);
        try { predicateProgram?.Dispose(); } catch (Exception error) { (errors ??= []).Add(error); }
        finally { predicateProgram = null; }
        try { layerProgram?.Dispose(); } catch (Exception error) { (errors ??= []).Add(error); }
        finally { layerProgram = null; }
        assertionManager = null;
        if (errors != null) throw new AggregateException("Checker shader cleanup failed.", errors);
    }
    #endregion
}
