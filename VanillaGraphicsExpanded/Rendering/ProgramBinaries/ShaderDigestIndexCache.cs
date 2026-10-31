using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.Rendering.Spirv;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.ProgramBinaries;

/// <summary>Shares one parsed digest index per asset source across all graphics and compute programs.</summary>
internal static class ShaderDigestIndexCache
{
    private static readonly object Gate = new();
    private static ConditionalWeakTable<IAssetManager, Dictionary<string, ShaderBinaryDigest.Manifest?>> assets = new();
    private static readonly Cache.Disk.IDiskCacheFileSystem physicalFiles = new Cache.Disk.RealDiskCacheFileSystem();
    private static ConditionalWeakTable<Cache.Disk.IDiskCacheFileSystem, Dictionary<string, ShaderBinaryDigest.Manifest?>> files = new();

    #region Shared reads
    /// <summary>Loads the application asset index once per domain, including caching unavailable metadata.</summary>
    internal static ShaderBinaryDigest.Manifest? ForAssets(IAssetManager owner, string domain)
    {
        lock (Gate)
        {
            var domains = assets.GetValue(owner, _ => new(StringComparer.Ordinal));
            if (domains.TryGetValue(domain, out var manifest)) return manifest;
            try
            {
                var asset = owner.TryGet(AssetLocation.Create("shaders/" + ShaderBinaryDigest.FileName, domain), loadAsset: true);
                manifest = asset?.Data == null ? null : ShaderBinaryDigest.Parse(asset.Data);
            }
            catch (Exception) { manifest = null; }
            domains.Add(domain, manifest);
            return manifest;
        }
    }

    /// <summary>Shares indexes for explicit binary-file loading until the next shader asset reload.</summary>
    internal static ShaderBinaryDigest.Manifest? ForFile(string path, Cache.Disk.IDiskCacheFileSystem? fileSystem = null)
    {
        path = Path.GetFullPath(path);
        lock (Gate)
        {
            // A provider owns its path namespace. An in-memory source must never donate a
            // cached manifest to the physical loader, or vice versa.
            var source = files.GetValue(fileSystem ?? physicalFiles, _ => new(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));
            if (source.TryGetValue(path, out var manifest)) return manifest;
            try { manifest = ShaderBinaryDigest.Parse((fileSystem ?? physicalFiles).ReadAllBytes(path)); }
            catch (Exception) { manifest = null; }
            source.Add(path, manifest);
            return manifest;
        }
    }
    #endregion

    #region Asset lifetime
    /// <summary>Drops shared indexes at an asset reload or application lifetime boundary, never at program creation.</summary>
    internal static void Clear()
    {
        lock (Gate)
        {
            assets = new();
            files = new();
        }
    }
    #endregion
}
