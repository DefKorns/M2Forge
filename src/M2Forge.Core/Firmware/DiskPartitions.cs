using System.Buffers.Binary;

namespace M2Forge.Core.Firmware;

public static class DiskPartitions
{
    const int SectorSize = 512;
    const int EntryOffset = 446;
    const int MaxLogical = 64;

    public static IReadOnlyList<DiskPartition> Read(string imagePath)
    {
        using var image = File.OpenRead(imagePath);
        var mbr = ReadSector(image, 0);
        if (mbr[510] != 0x55 || mbr[511] != 0xAA)
            throw new InvalidDataException("The image has no MBR partition table.");

        var result = new List<DiskPartition>();
        long extendedStart = 0;
        for (var i = 0; i < 4; i++)
        {
            var (type, start, count) = Entry(mbr, i);
            if (type == 0 || count == 0) continue;
            if (IsExtended(type))
            {
                extendedStart = start;
                continue;
            }
            result.Add(new DiskPartition(i + 1, start * SectorSize, count * SectorSize, type));
        }

        var next = extendedStart;
        var number = 5;
        for (var n = 0; extendedStart != 0 && n < MaxLogical; n++)
        {
            var ebr = ReadSector(image, next);
            var (type, start, count) = Entry(ebr, 0);
            if (type != 0 && count != 0)
                result.Add(new DiskPartition(number++, (next + start) * SectorSize, count * SectorSize, type));

            var (linkType, linkStart, _) = Entry(ebr, 1);
            if (!IsExtended(linkType) || linkStart == 0) break;
            next = extendedStart + linkStart;
        }
        return result;
    }

    static bool IsExtended(byte type) => type is 0x05 or 0x0F or 0x85;

    static (byte Type, long Start, long Count) Entry(byte[] sector, int index)
    {
        var entry = sector.AsSpan(EntryOffset + index * 16, 16);
        return (entry[4], BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]), BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]));
    }

    static byte[] ReadSector(FileStream image, long sector)
    {
        var buffer = new byte[SectorSize];
        image.Position = sector * SectorSize;
        image.ReadExactly(buffer);
        return buffer;
    }
}
