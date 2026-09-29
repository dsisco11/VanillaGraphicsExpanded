using System.Reflection;
using System.Text;
using TinyPreprocessor.Core;
using TinyTokenizer.Ast;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks that asset imports reuse parsed content without sharing mutable syntax trees.</summary>
public sealed class ShaderImportCacheTests
{
    #region Import resolution
    /// <summary>Import source is read once per reload generation and remains isolated between consumers.</summary>
    [Fact]
    public async Task ImportCacheReusesRootAndInvalidatesChangedContent()
    {
        byte[] source = Encoding.UTF8.GetBytes("#version 330 core\nfloat cachedValue = 1.0;\n");
        int assetLookups = 0;
        IAsset asset = Proxy<IAsset>((method, _) => method.Name switch
        {
            "get_Data" => source,
            "ToText" => Encoding.UTF8.GetString(source),
            _ => throw new NotSupportedException(method.Name)
        });
        IAssetManager assets = Proxy<IAssetManager>((method, _) =>
        {
            if (method.Name != "TryGet") throw new NotSupportedException(method.Name);
            assetLookups++;
            return asset;
        });
        var resolver = new AssetSyntaxTreeResourceResolver(assets, "vanillagraphicsexpanded");
        var parent = new Resource<SyntaxTree>(
            new ResourceId("vanillagraphicsexpanded:shaders/test.fsh"),
            SyntaxTree.Parse("", GlslSchema.Instance),
            new Dictionary<string, object>());

        var first = await resolver.ResolveAsync("./includes/shared.glsl", parent, TestContext.Current.CancellationToken);
        var second = await resolver.ResolveAsync("./includes/shared.glsl", parent, TestContext.Current.CancellationToken);
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, assetLookups);
        Assert.NotSame(first.Resource!.Content, second.Resource!.Content);
        Assert.Equal(first.Resource.Content.ToText(), second.Resource.Content.ToText());
        first.Resource.Content.CreateEditor()
            .InsertAfter(Query.Syntax<GlDirectiveNode>().Named("version"), "// local edit\n")
            .Commit();
        Assert.Contains("cachedValue", second.Resource.Content.ToText());
        Assert.DoesNotContain("local edit", second.Resource.Content.ToText());

        source = Encoding.UTF8.GetBytes("#version 330 core\nfloat cachedValue = 2.0;\n");
        var beforeReload = await resolver.ResolveAsync("./includes/shared.glsl", parent, TestContext.Current.CancellationToken);
        Assert.Contains("1.0", beforeReload.Resource!.Content.ToText());
        Assert.Equal(1, assetLookups);

        resolver.Clear();
        var changed = await resolver.ResolveAsync("./includes/shared.glsl", parent, TestContext.Current.CancellationToken);
        Assert.True(changed.IsSuccess);
        Assert.Contains("2.0", changed.Resource!.Content.ToText());
        Assert.DoesNotContain("local edit", changed.Resource.Content.ToText());
        Assert.Equal(2, assetLookups);
    }

    /// <summary>Concurrent cold requests parse once, while a missing asset remains retryable.</summary>
    [Fact]
    public async Task ConcurrentRequestsShareOneLoadAndMissingAssetsCanBeRetried()
    {
        int lookups = 0;
        IAsset? available = null;
        IAsset asset = Proxy<IAsset>((method, _) => method.Name switch
        {
            "ToText" => "float imported = 1.0;\n",
            _ => throw new NotSupportedException(method.Name)
        });
        IAssetManager assets = Proxy<IAssetManager>((method, _) =>
        {
            if (method.Name != "TryGet") throw new NotSupportedException(method.Name);
            Interlocked.Increment(ref lookups);
            return Volatile.Read(ref available);
        });
        var resolver = new AssetSyntaxTreeResourceResolver(assets, "vanillagraphicsexpanded");
        var parent = new Resource<SyntaxTree>(
            new ResourceId("vanillagraphicsexpanded:shaders/test.fsh"),
            SyntaxTree.Parse("", GlslSchema.Instance),
            new Dictionary<string, object>());

        var missing = await resolver.ResolveAsync("./includes/shared.glsl", parent, TestContext.Current.CancellationToken);
        Assert.False(missing.IsSuccess);
        Volatile.Write(ref available, asset);

        var requests = Enumerable.Range(0, 12).Select(_ => Task.Run(
            () => resolver.ResolveAsync("./includes/shared.glsl", parent, TestContext.Current.CancellationToken).Result));
        var results = await Task.WhenAll(requests);
        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(2, lookups);
    }
    #endregion

    #region Test API
    /// <summary>Creates a narrow engine interface proxy for the import resolver.</summary>
    private static T Proxy<T>(System.Func<MethodInfo, object?[]?, object?> invoke) where T : class
    {
        T proxy = DispatchProxy.Create<T, ApiProxy>();
        ((ApiProxy)(object)proxy).Handler = invoke;
        return proxy;
    }

    /// <summary>Forwards fixture calls to a configured handler.</summary>
    public class ApiProxy : DispatchProxy
    {
        public System.Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        /// <summary>Invokes the test fixture handler.</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
    #endregion
}
