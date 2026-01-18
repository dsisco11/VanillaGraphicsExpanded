# Material System Cache Architecture

> **Document**: MaterialSystem.Cache.Architecture.md
> **Status**: Draft
> **Dependencies**: PbrMaterialAtlas.Refactor.todo (Phase 7 interfaces)
> **Implements**: MaterialSystem.Cache.todo

---

## 1. Overview

The material system cache persists per-texture-rect bake results to disk so the
PBR material atlas can skip recomputation across sessions. The cache covers:

- Material params (RGB16F) for each atlas rect
- Normal + depth (RGBA16F) for each atlas rect

The cache is designed to be deterministic, low-risk, and aware of asset
provenance. It avoids hashing asset bytes and instead uses a lightweight
fingerprint based on asset metadata, config, and pack/mod lists.

---

## 2. Goals and Non-Goals

### 2.1 Goals

- Cache outputs for material params and normal+depth in the first iteration.
- Per-texture-rect granularity (no whole-page cache coupling).
- Deterministic cache keys based on fully resolved inputs.
- Global cache by default with a per-world overlay for mutable assets.
- Lightweight invalidation (no content hashing).
- Safe, atomic writes and robust handling of corruption.

### 2.2 Non-Goals

- Changing material mapping rules or shader formats.
- Changing the atlas layout or rect math.
- Storing arbitrary asset data or raw source textures.

---

## 3. High-Level Architecture

The cache system is split into four layers:

1. **Key Builder**: Generates deterministic cache keys from resolved inputs.
2. **Policy**: Decides whether to use global cache, per-world cache, or both.
3. **Store**: Reads/writes cache entries, handles LRU and disk IO.
4. **Integration**: Hooks into material params and normal+depth pipelines.

```text
Registry + Atlas Snapshot + Config
            |                 |
            v                 v
      CacheKeyBuilder     CachePolicy
            |                 |
            +----> CacheStore <-----+
                        |           |
                        v           |
                  CacheEntry        |
                        |           |
          MaterialParamsPipeline  NormalDepthPipeline
```

---

## 4. Cache Scope and Policy

### 4.1 Cache Tiers

- **Global cache**: Shared across worlds and sessions. Preferred when assets
  are stable.
- **Per-world cache**: Overlay used for mutable or unknown assets.

Read order: per-world first, then global. Writes:

- Stable assets: write to global cache.
- Mutable or unknown assets: write to per-world cache.

Cache root location:

- `VintagestoryData/VGE/Cache/` (global)
- `VintagestoryData/VGE/Cache/Worlds/<worldId>/` (per-world overlay)

### 4.2 Asset Provenance Rules

Assets are classified as:

- **Stable**: Base game or mod assets not overridden by packs.
- **Mutable**: Resource pack overrides, server/world mods, or unknown provenance.

If provenance is unknown, treat as mutable.

---

## 5. Cache Key Schema

### 5.1 Shared Fields

All keys include:

- Schema version
- Atlas rect size (width, height)
- Normalized asset location (domain + path)
- Pack list fingerprint
- Mod list fingerprint
- Material definition fingerprint

### 5.2 Material Params Key Fields

- Roughness, metallic, emissive (resolved)
- Noise (roughness, metallic, emissive)
- Scale (roughness, metallic, emissive)
- Override identity + override scale (if any)
- Bake version stamp (blue-noise seed/size, algorithm constants)

### 5.3 Normal + Depth Key Fields

All material params fields plus:

- Normal + depth scale
- NormalDepthBakeConfig values
- Source albedo fingerprint (path + metadata)

---

## 6. Lightweight Fingerprints

No asset byte hashing is used. Fingerprints are built from:

- Asset location (domain + path)
- Asset source id (mod id or pack name)
- Last modified timestamp (if available)
- File size (if available)

Namespace fingerprints:

- **Pack list fingerprint**: ordered pack ids + timestamps
- **Mod list fingerprint**: ordered mod ids + versions
- **Material definitions fingerprint**: material_definitions timestamp or version

If metadata is unavailable, the fingerprint falls back to asset location only,
which forces a more conservative cache policy (per-world only).

---

## 7. Cache Store Format

### 7.1 File Layout

Per-key cache entry uses a `.dds` texture file for the payload, plus a small
sidecar header for metadata:

```text
<key>.dds   -> texture payload (RGB16F or RGBA16F, DDS container)
<key>.meta  -> schema version, payload kind, key hash, timestamp, fingerprints
```

The `.dds` payload stores float16 channels:

- RGB16F cache: interleaved RGB triplets
- RGBA16F cache: interleaved RGBA

Optional compression can be added later; first iteration uses uncompressed DDS
to simplify IO and reduce decoding overhead.

### 7.2 Atomic Writes

- Write `.dds` and `.meta` to temp files, then move/replace.
- On read, reject files with invalid header or size mismatch.

---

## 8. Integration Points

### 8.1 Material Params

- Before CPU bake, build cache key and probe cache.
- Cache hit: enqueue upload immediately.
- Cache miss: compute tile, upload, then persist result.
- Cache stores post-override RGB data to avoid reapplying overrides.

### 8.2 Normal + Depth

- Before GPU bake, probe cache per rect.
- Upload cached rects and bake only misses.
- Read back baked misses and persist.
- Overrides are cached as authoritative output.

### 8.3 Scheduler and Threading

- File IO runs off the render thread.
- Upload remains on render thread as today.
- Session generation id guards against stale uploads or cache writes.

---

## 9. Invalidation and Eviction

- Schema changes or bake version changes invalidate entries.
- Pack/mod list changes invalidate namespaces.
- Material definition changes invalidate related entries.
- LRU eviction keeps cache size under limit.

---

## 10. Diagnostics

- Cache hit/miss counters for material params and normal+depth.
- Bytes on disk and eviction counts.
- Debug logging gate for cache activity.

---

## 11. Open Decisions

- Exact provenance signals available from `IAsset` and the API.
- Readback granularity for normal+depth (per-rect vs per-page).
- Thresholds for treating metadata as "unknown".
