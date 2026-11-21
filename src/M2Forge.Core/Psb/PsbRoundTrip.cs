using GMWare.M2.Psb;
using Newtonsoft.Json;

namespace M2Forge.Core.Psb;

public static class PsbRoundTrip
{
    public static PsbRoundTripResult Run(string psbPath, bool optimize = true)
    {
        var original = File.ReadAllBytes(psbPath);

        using var source = new MemoryStream(original);
        using var reader = new PsbReader(source);
        var root = reader.Root;
        reader.LoadAllStreamData();

        var writer = new PsbWriter(root, null)
        {
            Version = reader.Version,
            Flags = reader.Flags,
            Optimize = optimize,
        };
        using var rewritten = new MemoryStream();
        writer.Write(rewritten);
        var bytes = rewritten.ToArray();

        var before = Snapshot(original);
        var after = Snapshot(bytes);

        return new PsbRoundTripResult(
            psbPath,
            reader.Version,
            reader.Flags,
            original.AsSpan().SequenceEqual(bytes),
            before.Json == after.Json,
            StreamsEqual(before.Streams, after.Streams),
            original.Length,
            bytes.Length);
    }

    static (string Json, SortedDictionary<string, byte[]> Streams) Snapshot(byte[] psb)
    {
        using var source = new MemoryStream(psb);
        using var reader = new PsbReader(source);
        var root = reader.Root;
        reader.LoadAllStreamData();

        using var text = new StringWriter();
        using (var json = new JsonTextWriter(text))
            root.WriteTo(json);

        var streams = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var stream in reader.StreamCache) streams["s" + stream.Key] = stream.Value.BinaryData;
        foreach (var stream in reader.BStreamCache) streams["b" + stream.Key] = stream.Value.BinaryData;
        return (text.ToString(), streams);
    }

    static bool StreamsEqual(SortedDictionary<string, byte[]> a, SortedDictionary<string, byte[]> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var other) && other.AsSpan().SequenceEqual(kv.Value));
}
