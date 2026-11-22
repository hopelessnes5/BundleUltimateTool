using BundleFormat.Primitives;

namespace BundleFormat.Models;

/// <summary>32-byte S_NFSModelHeader; the uint32 mesh-offset array follows immediately at byte 32.</summary>
public readonly record struct NfsModelHeader(ushort Unknown, ushort NumMeshes)
{
    public const int Size = 32;

    public static NfsModelHeader Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Size)
            throw new BundleFormatException($"NFS model header needs {Size} bytes, got {bytes.Length}.");
        return new NfsModelHeader(
            EndianReader.ReadUInt16(bytes, 16, "model.unknown"),
            EndianReader.ReadUInt16(bytes, 18, "model.numMeshes"));
    }

    public static uint[] ReadMeshHeaderOffsets(ReadOnlySpan<byte> headerData, int numMeshes)
    {
        if (numMeshes < 0) throw new ArgumentOutOfRangeException(nameof(numMeshes));
        EndianReader.EnsureAvailable(headerData.Length, Size, checked(numMeshes * 4), "model.meshOffsets");
        var offsets = new uint[numMeshes];
        for (int i = 0; i < numMeshes; i++)
            offsets[i] = EndianReader.ReadUInt32(headerData, Size + i * 4, $"model.meshOffsets[{i}]");
        return offsets;
    }
}
