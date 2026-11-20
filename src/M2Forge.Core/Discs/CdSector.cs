using System.Buffers.Binary;

namespace M2Forge.Core.Discs;

public static class CdSector
{
    public const int DataSize = 2048;
    public const int RawSize = 2352;
    public const int PregapFrames = 150;

    const int EdcOffset = 2064;
    const int EccPOffset = 2076;
    const int EccQOffset = 2248;
    const int BlockStart = 12;

    static readonly byte[] EccForward = new byte[256];
    static readonly byte[] EccBackward = new byte[256];
    static readonly uint[] EdcTable = new uint[256];

    static CdSector()
    {
        for (var i = 0; i < 256; i++)
        {
            var j = (i << 1) ^ ((i & 0x80) != 0 ? 0x11D : 0);
            EccForward[i] = (byte)j;
            EccBackward[i ^ j] = (byte)i;

            var edc = (uint)i;
            for (var bit = 0; bit < 8; bit++) edc = (edc >> 1) ^ ((edc & 1) != 0 ? 0xD8018001u : 0);
            EdcTable[i] = edc;
        }
    }

    public static uint ComputeEdc(ReadOnlySpan<byte> bytes)
    {
        uint edc = 0;
        foreach (var b in bytes) edc = (edc >> 8) ^ EdcTable[(edc ^ b) & 0xFF];
        return edc;
    }

    public static byte ToBcd(int value) => (byte)((value / 10 << 4) | (value % 10));

    public static int MsfToLba(byte minutes, byte seconds, byte frames) =>
        FromBcd(minutes) * 75 * 60 + FromBcd(seconds) * 75 + FromBcd(frames) - PregapFrames;

    public static void WriteMode1(ReadOnlySpan<byte> data, int lba, Span<byte> sector)
    {
        if (data.Length != DataSize) throw new ArgumentException("A Mode 1 sector carries exactly 2048 bytes.", nameof(data));
        if (sector.Length != RawSize) throw new ArgumentException("A raw sector is 2352 bytes.", nameof(sector));

        sector.Clear();
        sector[1..11].Fill(0xFF);

        var frame = lba + PregapFrames;
        sector[12] = ToBcd(frame / (75 * 60));
        sector[13] = ToBcd(frame / 75 % 60);
        sector[14] = ToBcd(frame % 75);
        sector[15] = 0x01;
        data.CopyTo(sector[16..]);

        BinaryPrimitives.WriteUInt32LittleEndian(sector[EdcOffset..], ComputeEdc(sector[..EdcOffset]));

        Block(sector[BlockStart..], 86, 24, 2, 86, sector[EccPOffset..]);
        Block(sector[BlockStart..], 52, 43, 86, 88, sector[EccQOffset..]);
    }

    public static bool IsValidMode1(ReadOnlySpan<byte> sector)
    {
        if (sector.Length != RawSize) return false;

        Span<byte> expected = stackalloc byte[RawSize];
        WriteMode1(sector.Slice(16, DataSize), MsfToLba(sector[12], sector[13], sector[14]), expected);
        return sector.SequenceEqual(expected);
    }

    static int FromBcd(byte value) => (value >> 4) * 10 + (value & 0x0F);

    static void Block(ReadOnlySpan<byte> source, int majorCount, int minorCount, int majorMultiplier, int minorIncrement, Span<byte> destination)
    {
        var size = majorCount * minorCount;
        for (var major = 0; major < majorCount; major++)
        {
            var index = (major >> 1) * majorMultiplier + (major & 1);
            byte eccA = 0;
            byte eccB = 0;
            for (var minor = 0; minor < minorCount; minor++)
            {
                var value = source[index];
                index += minorIncrement;
                if (index >= size) index -= size;
                eccA ^= value;
                eccB ^= value;
                eccA = EccForward[eccA];
            }
            eccA = EccBackward[EccForward[eccA] ^ eccB];
            destination[major] = eccA;
            destination[major + majorCount] = (byte)(eccA ^ eccB);
        }
    }
}
