using GMWare.M2.Psb;
using M2Forge.Core.Psb;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace M2Forge.Tests.Psb;

public class PsbStreamEditorTests
{
    static byte[] BuildPsb(ushort version, params byte[][] streams)
    {
        var root = new JObject { ["id"] = "test", ["name"] = "sample" };
        for (var i = 0; i < streams.Length; i++)
            root["pixel" + i] = JStream.FromBytes(streams[i], false);

        using var output = new MemoryStream();
        new PsbWriter(root, null) { Version = version, Optimize = false }.Write(output);
        return output.ToArray();
    }

    static Dictionary<uint, byte[]> ReadStreams(byte[] psb, out string json)
    {
        using var source = new MemoryStream(psb);
        using var reader = new PsbReader(source);
        var root = reader.Root;
        reader.LoadAllStreamData();

        using var text = new StringWriter();
        using (var writer = new JsonTextWriter(text)) root.WriteTo(writer);
        json = text.ToString();
        return reader.StreamCache.ToDictionary(kv => kv.Key, kv => kv.Value.BinaryData);
    }

    [Theory]
    [InlineData((ushort)2)]
    [InlineData((ushort)3)]
    [InlineData((ushort)4)]
    public void Locate_FindsStreamsAtTheirRealPosition(ushort version)
    {
        byte[] first = [1, 2, 3, 4, 5], second = [9, 8, 7];
        var psb = BuildPsb(version, first, second);

        var editor = new PsbStreamEditor(psb);

        var read = ReadStreams(psb, out _);
        Assert.Equal(read.Count, editor.Streams.Count);
        foreach (var stream in editor.Streams)
            Assert.Equal(read[stream.Index], editor.Read(stream.Kind, stream.Index).ToArray());
    }

    [Fact]
    public void Replace_ChangesOnlyTheStreamBytes()
    {
        byte[] first = [1, 2, 3, 4, 5], second = [9, 8, 7];
        var original = BuildPsb(4, first, second);
        var editor = new PsbStreamEditor(original);
        var target = editor.Streams.Single(s => editor.Read(s.Kind, s.Index).Span.SequenceEqual(second));

        editor.Replace(target.Kind, target.Index, [0xAA, 0xBB, 0xCC]);
        var patched = editor.ToArray();

        var changed = Enumerable.Range(0, original.Length).Where(i => original[i] != patched[i]).ToList();
        Assert.Equal(Enumerable.Range(target.Offset, target.Length), changed);

        var before = ReadStreams(original, out var jsonBefore);
        var after = ReadStreams(patched, out var jsonAfter);
        Assert.Equal(jsonBefore, jsonAfter);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, after[target.Index]);
        foreach (var (key, bytes) in before.Where(kv => kv.Key != target.Index))
            Assert.Equal(bytes, after[key]);
    }

    [Fact]
    public void Replace_RejectsDifferentLength()
    {
        var editor = new PsbStreamEditor(BuildPsb(4, [1, 2, 3]));
        var stream = Assert.Single(editor.Streams);

        Assert.Throws<ArgumentException>(() => editor.Replace(stream.Kind, stream.Index, [1, 2]));
        Assert.Throws<ArgumentException>(() => editor.Replace(stream.Kind, stream.Index, [1, 2, 3, 4]));
    }

    [Fact]
    public void Constructor_RejectsGarbageAndFilteredFiles()
    {
        Assert.Throws<InvalidDataException>(() => new PsbStreamEditor(new byte[64]));

        var filtered = BuildPsb(4, [1]);
        filtered[6] = 1;
        Assert.Throws<NotSupportedException>(() => new PsbStreamEditor(filtered));
    }

    [Fact]
    public void Read_UnknownStreamThrows()
    {
        var editor = new PsbStreamEditor(BuildPsb(4, [1]));
        Assert.Throws<KeyNotFoundException>(() => editor.Read(PsbStreamKind.Stream, 5));
    }

    [FirmwareFact]
    public void EveryFirmwarePsb_StreamsMatchTheReader()
    {
        var reference = Environment.GetEnvironmentVariable("M2FORGE_TEST_REFERENCE");
        Assert.True(!string.IsNullOrEmpty(reference) && Directory.Exists(reference), "Set M2FORGE_TEST_REFERENCE.");

        var checkedStreams = 0;
        var mismatches = new List<string>();
        foreach (var file in Directory.EnumerateFiles(reference!, "*.psb", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(file);
            var editor = new PsbStreamEditor(bytes);

            using var source = new MemoryStream(bytes);
            using var reader = new PsbReader(source);
            _ = reader.Root;
            reader.LoadAllStreamData();

            var expected = reader.StreamCache.Select(kv => (PsbStreamKind.Stream, kv.Key, kv.Value.BinaryData))
                .Concat(reader.BStreamCache.Select(kv => (PsbStreamKind.BStream, kv.Key, kv.Value.BinaryData)))
                .ToList();

            if (expected.Count != editor.Streams.Count)
                mismatches.Add($"{file}: {editor.Streams.Count} located, {expected.Count} read");

            foreach (var (kind, index, data) in expected)
            {
                checkedStreams++;
                if (!editor.Read(kind, index).Span.SequenceEqual(data))
                    mismatches.Add($"{file}: {kind} {index}");
            }
        }

        Assert.Empty(mismatches);
        Assert.True(checkedStreams > 100);
    }

    [FirmwareFact]
    public void FirmwarePsb_PatchWithSameBytesLeavesFileIdentical_AndPatchTouchesOnlyThatStream()
    {
        var reference = Environment.GetEnvironmentVariable("M2FORGE_TEST_REFERENCE")!;
        var path = Path.Combine(reference, "system", "motion", "titleselect_ui.psb");
        var original = File.ReadAllBytes(path);

        var same = new PsbStreamEditor(original);
        foreach (var stream in same.Streams)
            same.Replace(stream.Kind, stream.Index, same.Read(stream.Kind, stream.Index).ToArray());
        Assert.Equal(original, same.ToArray());

        var editor = new PsbStreamEditor(original);
        var target = editor.Streams.OrderByDescending(s => s.Length).First();
        var altered = editor.Read(target.Kind, target.Index).ToArray();
        for (var i = 0; i < altered.Length; i++) altered[i] ^= 0x5A;
        editor.Replace(target.Kind, target.Index, altered);
        var patched = editor.ToArray();

        Assert.Equal(original.Length, patched.Length);
        var changed = Enumerable.Range(0, original.Length).Where(i => original[i] != patched[i]).ToList();
        Assert.Equal(altered.Length, changed.Count);
        Assert.All(changed, i => Assert.InRange(i, target.Offset, target.Offset + target.Length - 1));

        var read = ReadStreams(patched, out var jsonAfter);
        ReadStreams(original, out var jsonBefore);
        Assert.Equal(jsonBefore, jsonAfter);
        Assert.Equal(altered, read[target.Index]);
    }
}
