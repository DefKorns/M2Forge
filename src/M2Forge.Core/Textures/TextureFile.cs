namespace M2Forge.Core.Textures;

public sealed record TextureFile(string Name, IReadOnlyList<TextureSource> Sources);
