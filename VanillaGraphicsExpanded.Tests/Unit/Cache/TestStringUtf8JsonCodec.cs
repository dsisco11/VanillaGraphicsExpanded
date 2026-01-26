using System;
using System.Text;

using VanillaGraphicsExpanded.Cache;

namespace VanillaGraphicsExpanded.Tests.Unit.Cache;

public sealed class TestStringUtf8JsonCodec : ICacheCodec<string>
{
    public int SchemaVersion => 1;

    public bool TryEncode(in string payload, out byte[] bytes)
    {
        bytes = Encoding.UTF8.GetBytes(payload);
        return true;
    }

    public bool TryDecode(ReadOnlySpan<byte> bytes, out string payload)
    {
        payload = Encoding.UTF8.GetString(bytes);
        return true;
    }
}
