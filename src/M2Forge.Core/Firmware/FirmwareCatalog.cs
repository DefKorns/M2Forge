namespace M2Forge.Core.Firmware;

public static class FirmwareCatalog
{
    public static KnownFirmware? FindBySize(long binSize) =>
        binSize > 0 ? All.SingleOrDefault(f => f.BinSize == binSize) : null;

    public static IReadOnlyList<KnownFirmware> All { get; } =
    [
        new("Mega Drive Mini, Europe v0.8.3", "1085EU-1f2365d",
            "757025d54ab9c68d2c576ad9c3b8d63f84ce98195ff4ad2fe4360fecf8748378",
            "68e2ab12b7d15baa13e6c1f9e6146930c80655656cd2a5acecafbba495bd201b",
            "30e12b766ebf1cf19aac9195f96aaf2bff0650082fe87af90a0db4cc6be00698",
            "7f639c5d42e351ce30538435f247a58b3428361437c771dbff829c005d9523e1",
            BinSize: 110057472),
        new("Mega Drive Mini, Asia v0.8.3", "1085AS-1f2365d",
            "a7b0db436818596578e1ab8a750338842fe823bb388d3e01587ced785465bc64",
            "350c347f6001a7b2367e0b9cfc654c06cf726610ebde737e66d40cbfb3bc92d1",
            "30e12b766ebf1cf19aac9195f96aaf2bff0650082fe87af90a0db4cc6be00698",
            "52b1e1d547c7708b15701d48831759599b6b9bd83cd1c346d1077931260b71a3",
            BinSize: 100788224),
        new("Mega Drive Mini, Japan v0.8.3", "1085JP-1f2365d",
            "c7f268b939d4f2ff0f6b1375c2fb665f1536ccc51f09267f8493eb91aba9bc1d",
            "2c1f5735964f42c3bc225907ce059bea5a2e7d35e69467667cf3f73f3458037b",
            "30e12b766ebf1cf19aac9195f96aaf2bff0650082fe87af90a0db4cc6be00698",
            "3f23e1527b2a2f886efefdebffa68b309fd94ded4bbd2e1041af9ab0f30d9fb6",
            BinSize: 104835072),
        new("Mega Drive Mini, North America v0.8.3", "1080US-0d16385",
            "0c5e6fe9401f09ed3d0808f1f3ff762154ef9bc2f8fae596632a4b308c1ba15e",
            "9b1691b15522a2795549ede8800c1844e20acd328a03651c282956fb8b36f7dd",
            "b0dd76e6ab694ad4f5cf6e1db2a8aac87bcd1d28c87de07e454f464ebbe2527a",
            "a1edd69bf5a83aa3512a9229202240735e5c66b72e6138a569005f1a405e4759",
            BinSize: 107493376),
        new("Mega Drive Mini 2, Europe 0371EU", "0371EU",
            "8c1f49965d7411ac3d53488d2510e1c17b122121b768245706dbf26e63940772",
            "8a28cdb8f5def400e92ab1efb061848c9b52a32b37099e953c4095adf1810774",
            "2374ddca2241d2a040f587a1e86359c3d3d5f1dc93e566588b35e15bee73178f",
            string.Empty,
            ConsoleKind.MegaDriveMini2,
            4755216384),
    ];
}
