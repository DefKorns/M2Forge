using GMWare.M2.Psb;
using M2Forge.Core.Psb;

namespace M2Forge.Core.Textures;

public sealed record TextureSource(
    string Name,
    TextureFormat Format,
    int Width,
    int Height,
    string StreamPath,
    IReadOnlyList<IconInfo> Icons,
    PsbStreamKind StreamKind = PsbStreamKind.Stream,
    uint StreamIndex = 0)
{
    public RgbaImage Load() => RgbaImage.Decode(Format, File.ReadAllBytes(StreamPath), Width, Height);
}
