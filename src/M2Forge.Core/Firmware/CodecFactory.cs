using GMWare.M2.MArchive;

namespace M2Forge.Core.Firmware;

public static class CodecFactory
{
    public static IMArchiveCodec Create(string name) => name switch
    {
        "zstd" => new ZstdSharpCodec(),
        "zlib" => new ZlibCodec(),
        "fastlz" => new FastLzCodec(),
        _ => throw new ArgumentException($"Unknown codec '{name}'.", nameof(name)),
    };
}
