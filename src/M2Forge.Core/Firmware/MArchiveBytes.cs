using System.Text;
using GMWare.M2.MArchive;

namespace M2Forge.Core.Firmware;

public sealed class MArchiveBytes(IMArchiveCodec codec, string seed, int keyLength)
{
    public MArchiveBytes(ConsoleProfile profile, string seed)
        : this(CodecFactory.Create(profile.Codec), seed, profile.KeyLength)
    {
    }

    public byte[] Decompress(string fileName, byte[] stored)
    {
        using var input = new MemoryStream(stored);
        using var header = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        if (stored.Length < 8 || header.ReadUInt32() != codec.Magic)
            throw new InvalidDataException($"'{fileName}' does not use this codec.");

        var length = header.ReadInt32();
        if (length < 0) throw new InvalidDataException($"'{fileName}' has an invalid length.");

        using var output = new MemoryStream(length);
        using (var crypto = new MArchiveCryptoStream(input, fileName, seed, keyLength))
        using (var decompressed = codec.GetDecompressionStream(crypto, length))
            decompressed.CopyTo(output);

        if (output.Length != length)
            throw new InvalidDataException($"'{fileName}' decompressed to {output.Length} bytes, expected {length}.");
        return output.ToArray();
    }

    public byte[] Compress(string fileName, byte[] data)
    {
        using var output = new MemoryStream();
        using (var header = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            header.Write(codec.Magic);
            header.Write(data.Length);
        }

        using (var crypto = new MArchiveCryptoStream(output, fileName, seed, keyLength))
        using (var compressed = codec.GetCompressionStream(crypto))
            compressed.Write(data, 0, data.Length);

        return output.ToArray();
    }
}
