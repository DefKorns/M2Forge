using GMWare.M2.MArchive;
using ZstdSharp;

namespace M2Forge.Core.Firmware;

public sealed class ZstdSharpCodec : IMArchiveCodec
{
    const int MaxLevel = 22;

    public uint Magic => 0x00737a6d;

    public Stream GetCompressionStream(Stream inStream) =>
        new CompressionStream(inStream, MaxLevel, 0, true);

    public Stream GetDecompressionStream(Stream inStream) =>
        new DecompressionStream(inStream, 0, true, true);

    public Stream GetDecompressionStream(Stream inStream, int decompressedLength) =>
        GetDecompressionStream(inStream);
}
