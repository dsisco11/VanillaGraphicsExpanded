using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Captures a complete bounded native entity publication and its body and held-item command intervals.</summary>
internal sealed class NativeShadowEntityScene : IDisposable
{
    private readonly NativeShadowEntityPreparation preparation;
    private readonly long frame;
    private readonly int viewCount;
    private readonly List<NativeShadowShapeSnapshot> shapes = new();
    private readonly List<NativeShadowHeldItemSnapshot> hands = new();
    private readonly List<NativeShadowItemSnapshot> groundItems = new();
    private readonly List<Part> parts = new();
    private bool disposed;

    #region Public API
    internal ImmutableArray<ShadowObjectDrawRange> Ranges { get; }
    internal long MetadataBytes { get; private set; }

    /// <summary>Uses the native preparation owner's enumeration; unknown renderer families reject whole-scene completeness.</summary>
    internal NativeShadowEntityScene(NativeShadowEntityPreparation preparation, ShadowDrawSettings settings,
        int firstCommand, int viewCount)
    {
        this.preparation = preparation; frame = preparation.Frame; this.viewCount = viewCount;
        var policy = settings.Freeze();
        if (!preparation.IsCurrent || preparation.Renderers.Length > policy.EntityVisits || firstCommand < 0 ||
            viewCount < 1 || viewCount > policy.ViewCapacity)
            throw new InvalidOperationException("Complete native entity preparation is unavailable.");
        long allowance = policy.MemoryMiB * 1048576L;
        var ranges = ImmutableArray.CreateBuilder<ShadowObjectDrawRange>();
        try
        {
            foreach (var renderer in preparation.Renderers)
            {
                if (renderer.GetType() == typeof(EntityItemRenderer))
                {
                    Charge(512, allowance);
                    var item = new NativeShadowItemSnapshot((EntityItemRenderer)renderer, preparation);
                    groundItems.Add(item);
                    if (item.Mesh is { } mesh) Append(null, null, item, mesh, ranges, firstCommand, viewCount, policy);
                    continue;
                }
                if (renderer.GetType() != typeof(EntityShapeRenderer) && renderer.GetType() != typeof(EntityPlayerShapeRenderer))
                    throw new NotSupportedException("The native entity renderer has no complete shadow input adapter: " + renderer.GetType().FullName);
                var shape = new NativeShadowShapeSnapshot((EntityShapeRenderer)renderer, preparation, allowance - MetadataBytes);
                Charge(shape.SnapshotBytes, allowance); shapes.Add(shape);
                Append(shape, null, null, shape.Mesh, ranges, firstCommand, viewCount, policy);
                for (int hand = 0; hand < 2; hand++)
                {
                    Charge(512, allowance);
                    var held = new NativeShadowHeldItemSnapshot(shape, hand != 0); hands.Add(held);
                    if (held.Mesh is { } mesh) Append(shape, held, null, mesh, ranges, firstCommand, viewCount, policy);
                }
            }
            Ranges = ranges.ToImmutable();
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Requires unchanged native membership, complete poses, materials and every captured texture submesh.</summary>
    internal bool IsCurrent
    {
        get
        {
            if (disposed || !preparation.IsCurrent || preparation.Frame != frame || preparation.Renderers.Length != shapes.Count + groundItems.Count) return false;
            foreach (var shape in shapes) if (!shape.IsCurrent) return false;
            foreach (var hand in hands) if (!hand.IsCurrent) return false;
            foreach (var item in groundItems) if (!item.IsCurrent) return false;
            foreach (var part in parts)
                if (!part.IsCurrent) return false;
            return true;
        }
    }

    /// <summary>Captures each native submesh/view draw while preserving the body's skinning and the item's rigid attachment.</summary>
    internal ImmutableArray<ShadowPreparedDraw> Prepare(ShadowEntityShader bodies, ShadowStaticShader items,
        IReadOnlyList<ShadowDrawView> views, ShadowViewBuffer viewBuffer, ShadowGeometrySelection selection,
        ShadowPagePool pages)
    {
        if (!IsCurrent || views.Count != selection.ViewCount || views.Count != viewCount)
            throw new InvalidOperationException("Native entity publication changed before drawing.");
        var draws = ImmutableArray.CreateBuilder<ShadowPreparedDraw>();
        try
        {
            for (int index = 0; index < parts.Count; index++)
            {
                var part = parts[index];
                if (selection.ObjectRange(index) != Ranges[index])
                    throw new InvalidOperationException("Native entity selection differs from its captured complete commands.");
                for (int view = 0; view < views.Count; view++)
                {
                    if (part.Hand is null && part.Item is null)
                        draws.Add(new NativeShadowEntityDraw(part.Shape!, part.Index, bodies, views[view], viewBuffer, view,
                            selection, index, pages));
                    else
                    {
                        if (part.Hand is { } hand) hand.Apply(items, views[view]);
                        else part.Item!.Apply(items, views[view]);
                        draws.Add(new NativeShadowStaticDraw(part.Geometry!, part.Texture, () => part.IsCurrent &&
                            (part.Hand?.IsCurrent ?? part.Item!.IsCurrent),
                            items, views[view], viewBuffer, view, selection, index, pages));
                    }
                }
            }
            return draws.ToImmutable();
        }
        catch { foreach (var draw in draws) draw.Dispose(); throw; }
    }

    /// <summary>Releases only private static input borrows after prepared draws and their submission fence retire.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var part in parts) part.Geometry?.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Charges fixed metadata and retained uniform captures before accepting another native submesh.</summary>
    private void Charge(long amount, long allowance)
    {
        if (amount < 0 || amount > allowance - MetadataBytes)
            throw new InvalidOperationException("Native entity metadata exceeds the drawing allowance.");
        MetadataBytes += amount;
    }

    /// <summary>Builds complete object commands from native texture submeshes without reading their geometry.</summary>
    private void Append(NativeShadowShapeSnapshot? shape, NativeShadowHeldItemSnapshot? hand, NativeShadowItemSnapshot? item, MultiTextureMeshRef mesh,
        ImmutableArray<ShadowObjectDrawRange>.Builder ranges, int first, int views, ShadowDrawSettings policy)
    {
        if (mesh.Disposed || mesh.meshrefs.Length != mesh.textureids.Length || mesh.meshrefs.Length > policy.RangeVisits - parts.Count)
            throw new InvalidOperationException("Native entity texture submeshes are incomplete or exceed the range allowance.");
        for (int index = 0; index < mesh.meshrefs.Length; index++)
        {
            int command = checked(first + parts.Count);
            if (command >= policy.CommandSlots / views || mesh.meshrefs[index] is not VAO vao ||
                vao.IndicesCount <= 0 || vao.IndicesCount % 3 != 0 || mesh.textureids[index] <= 0)
                throw new InvalidOperationException("Native entity command or material extent is unavailable.");
            bool rigid = hand is not null || item is not null;
            Charge(checked(512L + views * (rigid ? 512 : ShadowEntityShader.JointCapacity * 64L + 512)), policy.MemoryMiB * 1048576L);
            NativeShadowStaticGeometry? geometry = null;
            if (rigid)
            {
                using var positions = new EngineBufferReference(vao.xyzVboId);
                if (!positions.Reference.TryGetMetadata(out var metadata) || metadata.Capacity <= 0 || metadata.Capacity % 12 != 0)
                    throw new InvalidOperationException("Native item position storage is incomplete.");
                geometry = new(vao, checked((int)(metadata.Capacity / 12)));
            }
            parts.Add(new(shape, hand, item, mesh, index, vao, mesh.textureids[index], vao.IndicesCount, geometry));
            ranges.Add(new((uint)command, 0, (uint)vao.IndicesCount, 0, 1, (uint)(vao.IndicesCount / 3), (uint)parts.Count,
                shape?.Dimension ?? item!.Dimension));
        }
    }

    /// <summary>Retains one native submesh identity without owning or duplicating its mesh data.</summary>
    private sealed record Part(NativeShadowShapeSnapshot? Shape, NativeShadowHeldItemSnapshot? Hand, NativeShadowItemSnapshot? Item,
        MultiTextureMeshRef Owner, int Index, VAO Mesh, int Texture, int Indices, NativeShadowStaticGeometry? Geometry)
    {
        /// <summary>Checks both original texture-array membership and borrowed static storage when present.</summary>
        internal bool IsCurrent => !Owner.Disposed && Index < Owner.meshrefs.Length && Index < Owner.textureids.Length &&
            ReferenceEquals(Owner.meshrefs[Index], Mesh) && Owner.textureids[Index] == Texture &&
            !Mesh.Disposed && Mesh.IndicesCount == Indices && (Geometry is null || Geometry.IsCurrent);
    }
    #endregion
}
