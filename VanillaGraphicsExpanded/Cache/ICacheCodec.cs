using System;

namespace VanillaGraphicsExpanded.Cache;

public interface ICacheCodec<TPayload>
{
    int SchemaVersion { get; }

    bool TryEncode(in TPayload payload, out byte[] bytes);

    bool TryDecode(ReadOnlySpan<byte> bytes, out TPayload payload);
}
