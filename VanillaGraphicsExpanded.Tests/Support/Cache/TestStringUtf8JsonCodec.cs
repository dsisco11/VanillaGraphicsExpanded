using System;
using System.Text;

using VanillaGraphicsExpanded.Cache;

namespace VanillaGraphicsExpanded.Tests.Support.Cache;

/// <summary>Encodes test string values as UTF-8 JSON cache payloads without schema transformation.</summary>
public sealed class TestStringUtf8JsonCodec : ICacheCodec<string>
{
    /// <inheritdoc />
    public int SchemaVersion => 1;

    /// <inheritdoc />
    public bool TryEncode(in string payload, out byte[] bytes)
    {
        bytes = Encoding.UTF8.GetBytes(payload);
        return true;
    }

    /// <inheritdoc />
    public bool TryDecode(ReadOnlySpan<byte> bytes, out string payload)
    {
        payload = Encoding.UTF8.GetString(bytes);
        return true;
    }
}
