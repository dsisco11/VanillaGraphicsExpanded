using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Observes actual static submissions and checks declaration identity for each target/load/store/area contract.</summary>
internal sealed class StaticPassDeclarationCapture : IDisposable
{
    [ThreadStatic] private static StaticPassDeclarationCapture? active;
    private readonly Harmony harmony = new("Tests.StaticPassDeclarationCapture");
    private readonly Dictionary<(GpuFramebuffer Target, string Contract), RenderPassDesc> declarations = new();
    private readonly List<string> rebuilt = new();
    private int repetitions;

    #region Public API
    /// <summary>Starts an isolated observer without changing submission or native execution.</summary>
    internal StaticPassDeclarationCapture()
    {
        if (active is not null) throw new InvalidOperationException("Static declaration capture already active.");
        active = this;
        try
        {
            harmony.Patch(AccessTools.Method(typeof(RenderPass), nameof(RenderPass.Begin)),
                prefix: new HarmonyMethod(typeof(StaticPassDeclarationCapture), nameof(Observe)));
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Requires actual static submissions and at least one reused declaration while reporting any repeated allocation.</summary>
    internal void AssertReusable(int minimumTargets = 1, int minimumRepetitions = 1)
    {
        Assert.Empty(rebuilt);
        Assert.True(declarations.Keys.Select(key => key.Target).Distinct().Count() >= minimumTargets);
        Assert.True(repetitions >= minimumRepetitions);
    }

    /// <summary>Removes only this observer's patches after normal or exceptional completion.</summary>
    public void Dispose()
    {
        harmony.UnpatchAll(harmony.Id);
        active = null;
    }
    #endregion

    #region Private
    /// <summary>Groups immutable intentions by their complete value contract, separately for each physical target instance.</summary>
    private static void Observe(RenderPassDesc description)
    {
        if (active is not { } capture || description.Target is not StaticGpuFramebuffer) return;
        string contract = string.Join("|", description.Colors.Select(DescribeColor))
            + ";" + description.DepthStencil + ";" + description.Area;
        var key = (description.Target, contract);
        if (!capture.declarations.TryAdd(key, description))
        {
            capture.repetitions++;
            if (!ReferenceEquals(capture.declarations[key], description))
                capture.rebuilt.Add($"{description.Target.FboId}: {contract}");
        }
    }

    /// <summary>Includes exact typed clear payload bits so distinct clear intentions cannot share an observation key.</summary>
    private static string DescribeColor(RenderPassColor color)
    {
        string contract = $"{color.Attachment}:{color.Load}:{color.Store}:{color.DiscardOutput}:{color.SurfaceBuffer}";
        if (color.Clear is null) return contract;
        // Clear storage deliberately has no public inspection API. Inspect its immutable
        // primitive arrays in this observer without exposing test readback in production.
        foreach (var field in typeof(ColorClearValue).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
            if (field.GetValue(color.Clear) is Array values)
            {
                byte[] bytes = new byte[Buffer.ByteLength(values)];
                Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
                contract += $":{field.Name}:{Convert.ToHexString(bytes)}";
            }
        return contract;
    }
    #endregion
}
