namespace M2Forge.Core.Textures;

public sealed record TextureLibraryScan(IReadOnlyList<TextureFile> Files, IReadOnlyList<string> Failures)
{
    public int TextureCount => Files.Sum(f => f.Sources.Count);
}
