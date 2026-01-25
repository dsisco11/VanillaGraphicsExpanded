namespace VanillaGraphicsExpanded.Rendering;

internal readonly record struct GlColorMask
{
    // Bit layout (low 4 bits):
    // 0: R, 1: G, 2: B, 3: A
    public byte Bits { get; }

    public GlColorMask(byte bits)
    {
        Bits = (byte)(bits & 0b1111);
    }

    public static GlColorMask All => new(0b1111);

    public bool R => (Bits & (1 << 0)) != 0;
    public bool G => (Bits & (1 << 1)) != 0;
    public bool B => (Bits & (1 << 2)) != 0;
    public bool A => (Bits & (1 << 3)) != 0;

    public static GlColorMask FromRgba(bool r, bool g, bool b, bool a)
        => new((byte)((r ? (1 << 0) : 0)
            | (g ? (1 << 1) : 0)
            | (b ? (1 << 2) : 0)
            | (a ? (1 << 3) : 0)));
}
