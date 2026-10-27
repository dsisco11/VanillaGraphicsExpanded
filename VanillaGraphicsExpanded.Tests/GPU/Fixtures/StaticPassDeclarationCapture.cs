using VanillaGraphicsExpanded.Rendering.RHI;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Observes final owned-image submissions and checks realization reuse for repeated target/load/store/area contracts.</summary>
internal sealed class StaticPassDeclarationCapture : IDisposable
{
    [ThreadStatic] private static StaticPassDeclarationCapture? active;
    private readonly Harmony harmony = new("Tests.StaticPassDeclarationCapture");
    private readonly Dictionary<(string Target, string Contract), int> declarations = new();
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
            harmony.Patch(AccessTools.Method(typeof(RHICommandContext), nameof(RHICommandContext.BeginRenderPass)),
                postfix: new HarmonyMethod(typeof(StaticPassDeclarationCapture), nameof(Observe)));
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
    private static void Observe(RHIRenderPassDescription description)
    {
        if(active is not {} capture || !description.Surface.IsEmpty)return;
        string target=string.Join("|",description.Colors.Select(color=>DescribeImage(color.Image)))+";"+DescribeImage(description.DepthStencil);
        string contract=string.Join("|",description.Colors.Select(DescribeColor))+";"+description.Actions+";"+description.Area;
        Assert.True(StateCache.Current.TryGetCachedCurrentFramebuffer(OpenTK.Graphics.OpenGL.FramebufferTarget.DrawFramebuffer,out int realized));
        var key=(target,contract);
        if(!capture.declarations.TryAdd(key,realized))
        {
            capture.repetitions++;
            if(capture.declarations[key]!=realized)capture.rebuilt.Add(target+": "+contract);
        }
    }

    /// <summary>Identifies retained image selections without resolving metadata or querying the driver.</summary>
    private static string DescribeImage(RHIAttachment? image) => image is {} value
        ? $"{value.Texture.GetHashCode()}:{value.Renderbuffer.GetHashCode()}:{value.Subresource}:{value.Layered}" : "none";

    /// <summary>Includes exact clear scalar bits without inspecting private implementation fields.</summary>
    private static string DescribeColor(RHIColorAttachment color)
    {
        string contract=$"{color.AttachmentIndex}:{color.Load}:{color.Store}:{color.DiscardOutput}:{color.WindowBuffer}";
        if(color.Clear is not {} clear)return contract;
        if(!clear.FloatComponents.IsEmpty)return contract+":f:"+string.Join(",",clear.FloatComponents.ToArray().Select(BitConverter.SingleToInt32Bits));
        if(!clear.SignedComponents.IsEmpty)return contract+":i:"+string.Join(",",clear.SignedComponents.ToArray());
        return contract+":u:"+string.Join(",",clear.UnsignedComponents.ToArray());
    }
    #endregion
}
