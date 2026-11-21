namespace M2Forge.Core.Firmware;

public sealed record ConsoleProfile(ConsoleKind Kind, string Name, string Codename, string Codec, int KeyLength)
{
    public static ConsoleProfile MegaDriveMini { get; } =
        new(ConsoleKind.MegaDriveMini, "Mega Drive Mini", "MOON", "zstd", 64);

    public static ConsoleProfile MegaDriveMini2 { get; } =
        new(ConsoleKind.MegaDriveMini2, "Mega Drive Mini 2", "diana", "zstd", 64);

    public static IReadOnlyList<ConsoleProfile> All { get; } = [MegaDriveMini, MegaDriveMini2];
}
