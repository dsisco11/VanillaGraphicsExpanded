using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Tests.Component.Spirv.Cache;

/// <summary>Checks that a binding contract change invalidates the emitted shader variant key.</summary>
public sealed class ShaderIncrementalBuildTests
{
    #region Compiler identity

    /// <summary>A binding-only contract edit changes emitted source and therefore invalidates the compiled variant.</summary>
    [Fact]
    public void BindingContractChangeInvalidatesEmittedVariantKey()
    {
        const string source = "#version 450 core\nuniform sampler2D image; layout(location=0) out vec4 color; void main(){color=texture(image,vec2(0));}";
        var first = new VanillaGraphicsExpanded.Rendering.Contracts.GpuBindingContract();
        first.RegisterSamplerUnit("image", 1);
        var second = new VanillaGraphicsExpanded.Rendering.Contracts.GpuBindingContract();
        second.RegisterSamplerUnit("image", 2);
        var cache = new ShaderBuildTool.Spirv.ShaderVariantCache("unused", "compiler");
        string before = ShaderBuildTool.Spirv.ShaderSourceLayout.Apply(source, "fsh", first);
        string after = ShaderBuildTool.Spirv.ShaderSourceLayout.Apply(source, "fsh", second);
        Assert.NotEqual(cache.Key(before, "fragment", "main"), cache.Key(after, "fragment", "main"));
    }
    #endregion

}
