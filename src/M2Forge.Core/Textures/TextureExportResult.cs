namespace M2Forge.Core.Textures;

public sealed record TextureExportResult(int Textures, int Icons, IReadOnlyList<string> Failures);
