# Material System Cache Architecture

> **Document**: MaterialSystem.Cache.Architecture.md
> **Status**: Draft
> **Dependencies**: PbrMaterialAtlas.Refactor.todo (Phase 7 interfaces)
> **Implements**: MaterialSystem.Cache.todo

---

## 1. Overview

The material system cache persists per-texture-rect bake results to disk so the
PBR material atlas can skip recomputation across sessions. The cache covers:

- Material params (RGB in RGBA16_UNORM) for each atlas rect
- Normal + depth (RGBA16_UNORM) for each atlas rect

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

### 4.0 Configuration

The disk cache can be toggled via config:

- `EnableMaterialAtlasDiskCache`: `true` by default.

When disabled, the cache behaves as a no-op store (always miss; no writes).

### 4.0.1 Performance Notes

- Normal+depth cache writes require GPU readback (`glReadPixels`) for each baked miss tile.
- This is intentionally done during loading / atlas build time (not gameplay) and only for cache misses.
- If startup hitching becomes noticeable, prefer raising the cache hit rate (keep cache enabled; avoid frequent shader reloads) rather than increasing bake complexity.

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

Provenance signals (locked in):

- If the resolved `IAsset` indicates a resource pack source, treat as mutable.
- If the resolved `IAsset` indicates server/world override, treat as mutable.
- If the resolved `IAsset` indicates base game or mod asset and no pack override is present, treat as stable.
- If any of the above signals are unavailable, treat as mutable.
- Persist the resolved provenance in the cache `.meta` sidecar to avoid re-querying.

Metadata threshold rules (locked in):

- Global cache is allowed only when provenance is stable and a fingerprint is
  sufficiently stable (source id + at least one of last modified or size).
- If provenance is missing or ambiguous, treat as unknown and use per-world only.
- If pack/mod list fingerprints are unavailable, allow per-world caching but
  never global caching.
- Cache `.meta` must record which metadata fields were present so future reads
  preserve the same policy.

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

### 5.4 Key Serialization and Hashing

All cache keys are built by serializing a stable, deterministic UTF-8 string and hashing it.

Serialization rules (locked in):

- The stable key is a single string with fields delimited by `|`.
- The field order is fixed and must never change without bumping the schema version.
- Numbers use `InvariantCulture`.
- Floating point values are formatted with round-trip format (`R`) for stability.
- Asset locations serialize via `AssetLocation.ToString()`.

Key structure:

- `StablePrefix` comes first and captures session/config/registry inputs.
- `StablePrefix` is followed by per-tile fields.

Per-tile fields are appended in this order:

- `kind=...`
- `page=<atlasTextureId>`
- `rect=(x,y,width,height)`
- `tex=<asset location or (unknown)>`
- For material params: `def=(roughness,metallic,emissive)`, then `noise=(...)`, then `scale=(...)`
- For normal+depth: `scale=(normalScale,depthScale)`

Hashing rules (locked in):

- Compute `SHA-256(UTF8(stableKey))`.
- Interpret the first 8 bytes of the digest as a little-endian `ulong` to form `Hash64`.
- Logical key formatting (for logs/debugging) is `v<SchemaVersion>:<Hash64 as 16 lowercase hex digits>`.
- On disk, the filename stem MUST be filesystem-safe (Windows does not allow `:`), so it is encoded as `v<SchemaVersion>-<Hash64 as 16 lowercase hex digits>`.

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

Per-tile cache entry (one per atlas rect) uses a `.dds` texture file for the
payload, plus a small sidecar header for metadata:

```text
<key>.material.dds   -> material params payload (RGBA16_UNORM, DX10 DDS container)
<key>.material.meta  -> schema version, payload kind, key hash, timestamp, fingerprints

<key>.norm.dds       -> normal+depth payload (RGBA16_UNORM, DX10 DDS container)
<key>.norm.meta      -> schema version, payload kind, key hash, timestamp, fingerprints
```

The `.dds` payload stores float16 channels:

- Cache payload is stored as uncompressed `R16G16B16A16_UNORM` (DX10 DDS) for both kinds.
- Material params are written as RGBA16_UNORM with `A=1` and consumers ignore alpha.
- Normal+depth is written as RGBA16_UNORM (RGB = normalXYZ_01, A = height01).

Optional compression can be added later; first iteration uses uncompressed DDS
to simplify IO and reduce decoding overhead.

### 7.2 Atomic Writes

- Write `.dds` and `.meta` to temp files, then move/replace.
- On read, reject files with invalid header or size mismatch.

---

## 8. Integration Points

### 8.1 Material Params

- Before CPU bake, build cache key and probe cache per tile.
- Cache hit: enqueue upload immediately.
- Cache miss: compute tile, upload, then persist result.
- Cache stores post-override RGB data to avoid reapplying overrides.

### 8.2 Normal + Depth

- Before GPU bake, probe cache per tile.
- Upload cached rects and bake only misses.
- Read back baked misses per tile and persist.
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

## 11. Resolved Decisions

- All cache design decisions are resolved in sections 4-9.
