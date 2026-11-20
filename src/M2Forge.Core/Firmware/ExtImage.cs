using System.Buffers.Binary;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace M2Forge.Core.Firmware;

public sealed class ExtImage : IDisposable
{
    const int ExtentsFlag = 0x80000;
    const int Incompat64Bit = 0x80;
    const ushort ExtentMagic = 0xF30A;
    const uint RootInode = 2;

    readonly SafeFileHandle handle;
    readonly long baseOffset;
    readonly uint blockSize;
    readonly uint inodesPerGroup;
    readonly int inodeSize;
    readonly int descriptorSize;
    readonly bool is64Bit;
    readonly long descriptorTable;

    public ExtImage(string path, long baseOffset = 0)
    {
        this.baseOffset = baseOffset;
        handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        var sb = ReadAt(1024, 1024);
        if (BinaryPrimitives.ReadUInt16LittleEndian(sb.AsSpan(56)) != 0xEF53)
        {
            handle.Dispose();
            throw new InvalidDataException("Not an ext filesystem.");
        }

        blockSize = 1024u << (int)BinaryPrimitives.ReadUInt32LittleEndian(sb.AsSpan(24));
        inodesPerGroup = BinaryPrimitives.ReadUInt32LittleEndian(sb.AsSpan(40));
        var revision = BinaryPrimitives.ReadUInt32LittleEndian(sb.AsSpan(76));
        inodeSize = revision >= 1 ? BinaryPrimitives.ReadUInt16LittleEndian(sb.AsSpan(88)) : 128;
        is64Bit = (BinaryPrimitives.ReadUInt32LittleEndian(sb.AsSpan(96)) & Incompat64Bit) != 0;
        descriptorSize = is64Bit ? BinaryPrimitives.ReadUInt16LittleEndian(sb.AsSpan(254)) : 32;
        var firstDataBlock = BinaryPrimitives.ReadUInt32LittleEndian(sb.AsSpan(20));
        descriptorTable = (firstDataBlock + 1L) * blockSize;
    }

    public bool FileExists(string path) => Find(path) is not null;

    public void CopyTo(string path, Stream output)
    {
        var inode = Find(path) ?? throw new FileNotFoundException(path);
        var remaining = SizeOf(inode);
        var buffer = new byte[blockSize];
        foreach (var physical in Blocks(inode))
        {
            if (remaining <= 0) break;
            var count = (int)Math.Min(blockSize, remaining);
            if (physical == 0) Array.Clear(buffer);
            else RandomAccess.Read(handle, buffer, baseOffset + physical * blockSize);
            output.Write(buffer, 0, count);
            remaining -= count;
        }
    }

    byte[]? Find(string path)
    {
        var inode = ReadInode(RootInode);
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Lookup(inode, part);
            if (next == 0) return null;
            inode = ReadInode(next);
        }
        return inode;
    }

    public IReadOnlyList<string> List(string path)
    {
        var directory = Find(path) ?? throw new DirectoryNotFoundException(path);
        return Entries(directory).Select(e => e.Name).Where(n => n != "." && n != "..").ToList();
    }

    uint Lookup(byte[] directory, string name) =>
        Entries(directory).FirstOrDefault(e => e.Name == name).Inode;

    IEnumerable<(string Name, uint Inode)> Entries(byte[] directory)
    {
        var data = new MemoryStream();
        var remaining = SizeOf(directory);
        foreach (var physical in Blocks(directory))
        {
            if (remaining <= 0) break;
            data.Write(physical == 0 ? new byte[blockSize] : ReadAt(physical * blockSize, (int)blockSize));
            remaining -= blockSize;
        }

        var bytes = data.ToArray();
        var offset = 0;
        while (offset + 8 <= bytes.Length)
        {
            var inode = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
            var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 4));
            var nameLength = bytes[offset + 6];
            if (recordLength < 8) yield break;
            if (inode != 0) yield return (Encoding.UTF8.GetString(bytes, offset + 8, nameLength), inode);
            offset += recordLength;
        }
    }

    byte[] ReadInode(uint number)
    {
        var group = (number - 1) / inodesPerGroup;
        var index = (number - 1) % inodesPerGroup;
        var descriptor = ReadAt(descriptorTable + group * descriptorSize, descriptorSize);
        var table = (long)BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(8));
        if (is64Bit && descriptorSize >= 44)
            table |= (long)BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(40)) << 32;
        return ReadAt(table * blockSize + index * inodeSize, Math.Min(inodeSize, 256));
    }

    static long SizeOf(byte[] inode) =>
        BinaryPrimitives.ReadUInt32LittleEndian(inode.AsSpan(4)) |
        (long)BinaryPrimitives.ReadUInt32LittleEndian(inode.AsSpan(108)) << 32;

    IEnumerable<long> Blocks(byte[] inode)
    {
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(inode.AsSpan(32));
        var iblock = inode.AsSpan(40, 60).ToArray();
        return (flags & ExtentsFlag) != 0 ? ExtentBlocks(iblock) : MapBlocks(iblock);
    }

    IEnumerable<long> ExtentBlocks(byte[] root)
    {
        long next = 0;
        foreach (var run in ExtentRuns(root))
        {
            for (; next < run.Logical; next++) yield return 0;
            for (var j = 0; j < run.Length; j++) yield return run.Initialized ? run.Start + j : 0;
            next = run.Logical + run.Length;
        }
    }

    IEnumerable<(long Logical, int Length, long Start, bool Initialized)> ExtentRuns(byte[] node)
    {
        if (BinaryPrimitives.ReadUInt16LittleEndian(node) != ExtentMagic)
            throw new InvalidDataException("Bad extent header.");
        var entries = BinaryPrimitives.ReadUInt16LittleEndian(node.AsSpan(2));
        var depth = BinaryPrimitives.ReadUInt16LittleEndian(node.AsSpan(6));

        for (var i = 0; i < entries; i++)
        {
            var entry = node.AsSpan(12 + i * 12, 12);
            if (depth > 0)
            {
                var child = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]) |
                            (long)BinaryPrimitives.ReadUInt16LittleEndian(entry[8..]) << 32;
                foreach (var run in ExtentRuns(ReadAt(child * blockSize, (int)blockSize))) yield return run;
                continue;
            }

            int length = BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]);
            var initialized = length <= 32768;
            if (!initialized) length -= 32768;
            var start = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]) |
                        (long)BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]) << 32;
            yield return (BinaryPrimitives.ReadUInt32LittleEndian(entry), length, start, initialized);
        }
    }

    IEnumerable<long> MapBlocks(byte[] iblock)
    {
        for (var i = 0; i < 12; i++) yield return BinaryPrimitives.ReadUInt32LittleEndian(iblock.AsSpan(i * 4));
        for (var level = 1; level <= 3; level++)
        {
            var root = BinaryPrimitives.ReadUInt32LittleEndian(iblock.AsSpan((11 + level) * 4));
            foreach (var block in Indirect(root, level)) yield return block;
        }
    }

    IEnumerable<long> Indirect(uint block, int level)
    {
        var pointers = blockSize / 4;
        if (block == 0)
        {
            var span = (long)Math.Pow(pointers, level);
            for (long i = 0; i < span; i++) yield return 0;
            yield break;
        }

        var data = ReadAt(block * (long)blockSize, (int)blockSize);
        for (var i = 0; i < pointers; i++)
        {
            var child = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i * 4));
            if (level == 1) yield return child;
            else foreach (var inner in Indirect(child, level - 1)) yield return inner;
        }
    }

    byte[] ReadAt(long offset, int length)
    {
        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = RandomAccess.Read(handle, buffer.AsSpan(read), baseOffset + offset + read);
            if (n == 0) break;
            read += n;
        }
        return buffer;
    }

    public void Dispose() => handle.Dispose();
}
