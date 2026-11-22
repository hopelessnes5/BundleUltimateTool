using BundleFormat.Primitives;

namespace BundleFormat.Textures;

/// <summary>32-byte S_NFSTextureHeader, read field by field with explicit little-endian reads.</summary>
public readonly record struct NfsTextureHeader(
    uint Stuff0, uint Stuff1, uint Stuff2, uint Type,
    ushort Width, ushort Height, uint Stuff3,
    byte Unknown, byte NumMipmaps, ushort Stuff4, uint Stuff5)
{
    public const int Size = 32;

    public static NfsTextureHeader Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Size)
            throw new BundleFormatException($"NFS texture header needs {Size} bytes, got {bytes.Length}.");
        return new NfsTextureHeader(
            EndianReader.ReadUInt32(bytes, 0, "texture.stuff0"),
            EndianReader.ReadUInt32(bytes, 4, "texture.stuff1"),
            EndianReader.ReadUInt32(bytes, 8, "texture.stuff2"),
            EndianReader.ReadUInt32(bytes, 12, "texture.type"),
            EndianReader.ReadUInt16(bytes, 16, "texture.width"),
            EndianReader.ReadUInt16(bytes, 18, "texture.height"),
            EndianReader.ReadUInt32(bytes, 20, "texture.stuff3"),
            EndianReader.ReadByte(bytes, 24, "texture.unknown"),
            EndianReader.ReadByte(bytes, 25, "texture.numMipmaps"),
            EndianReader.ReadUInt16(bytes, 26, "texture.stuff4"),
            EndianReader.ReadUInt32(bytes, 28, "texture.stuff5"));
    }

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new BundleFormatException($"NFS texture header needs {Size} bytes, got {destination.Length}.");
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), Stuff0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), Stuff1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), Stuff2);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12, 4), Type);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(16, 2), Width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(18, 2), Height);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(20, 4), Stuff3);
        destination[24] = Unknown;
        destination[25] = NumMipmaps;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(26, 2), Stuff4);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(28, 4), Stuff5);
    }

    public string TypeLabel => Type switch
    {
        DdsCodec.FourCcDxt1 => "DXT1",
        DdsCodec.FourCcDxt5 => "DXT5",
        DdsCodec.NfsTypeRgba32 => "RGBA32 (21)",
        _ => $"unknown ({Type})"
    };
}
