using M2Forge.Core.Roms;

namespace M2Forge.Tests.Roms;

public sealed class RomCatalogTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "m2forge-cat-" + Guid.NewGuid().ToString("N"));

    void WriteConfig(string profile, string top, string region = "032")
    {
        var config = Path.Combine(root, region, "config");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "title_prof.psb.json"), profile);
        File.WriteAllText(Path.Combine(config, "title_mode_top.psb.json"), top);
    }

    const string Profile = """
        {"root":{"m2epi":{"version":{
          "EU_EN_foo":{"rom":"roms/us_us_foo.bin","country":"us"},
          "EU_JP_foo":{"rom":"roms/jp_jp_foo.bin","country":"jp"},
          "EU_EN_bar":{"rom":"roms/eu_eu_bar_baz.bin","country":"eu"}
        }}}}
        """;

    const string Top = """
        {"items":[
          {"regionTag":"EU_EN_foo","tname":"Foo Game","image":3},
          {"regionTag":"EU_JP_foo","tname":"フーゲーム","image":3},
          {"regionTag":"EU_EN_bar","tname":"","image":99},
          {"regionTag":"DUMMY","tname":"","image":99}
        ]}
        """;

    [Fact]
    public void Load_LinksEachRomToItsOfficialName()
    {
        WriteConfig(Profile, Top);

        var catalog = RomCatalog.Load(root);

        Assert.Equal(3, catalog.Count);
        Assert.Equal("032", Path.GetFileName(catalog.RegionFolder));
        Assert.Equal("Foo Game", catalog.Find("us_us_foo.bin")!.Title);
        Assert.Null(catalog.Find("us_us_foo.bin")!.NativeTitle);
    }

    [Fact]
    public void ARomWithOnlyANativeTitle_KeepsItAsASubtitleAndPrettifiesTheFileName()
    {
        WriteConfig(Profile, Top);

        var details = RomCatalog.Load(root).Find("jp_jp_foo.bin")!;

        Assert.Equal("Foo", details.Title);
        Assert.Equal("フーゲーム", details.NativeTitle);
    }

    [Fact]
    public void ARomWithoutAnImage_HasNoCoverEntry()
    {
        WriteConfig(Profile, Top);

        var details = RomCatalog.Load(root).Find("eu_eu_bar_baz.bin")!;

        Assert.Equal("Bar Baz", details.Title);
        Assert.Null(details.CoverEntry);
    }

    [Fact]
    public void TheCoverEntry_PrefersTheEnglishMenuAndCarriesTheImageIndex()
    {
        WriteConfig(Profile, Top);

        var entry = RomCatalog.Load(root).Find("us_us_foo.bin")!.CoverEntry!;

        Assert.Equal("EN", entry.MenuLanguage);
        Assert.Equal("EU", entry.MenuRegion);
        Assert.Equal(3, entry.Image);
    }

    const string AlternateProfile = """
        {"root":{"m2epi":{"version":{
          "EU_EN_fooban":{"rom":"roms/us_us_foo.bin","country":"us"},
          "EU_JP_fooban":{"rom":"roms/jp_jp_foo.bin","country":"jp"},
          "EU_EN_fooba2":{"rom":"roms/us_us_foo_alt.bin","country":"us"},
          "EU_JP_fooba2":{"rom":"roms/jp_jp_foo_alt.bin","country":"jp"},
          "EU_EN_zzzzzz":{"rom":"roms/us_us_other.bin","country":"us"}
        }}}}
        """;

    const string AlternateTop = """
        {"items":[
          {"regionTag":"EU_EN_fooban","tname":"Foo Ban","image":7},
          {"regionTag":"EU_JP_fooban","tname":"フーバン","image":7}
        ]}
        """;

    [Fact]
    public void AnAlternateVersion_SharesTheCoverOfItsMenuSlotButKeepsItsOwnName()
    {
        WriteConfig(AlternateProfile, AlternateTop);

        var catalog = RomCatalog.Load(root);
        var alternate = catalog.Find("us_us_foo_alt.bin")!;

        Assert.Equal(7, alternate.CoverEntry!.Image);
        Assert.Equal("Foo Alt", alternate.Title);
        Assert.Null(alternate.NativeTitle);
        Assert.Equal(7, catalog.Find("jp_jp_foo_alt.bin")!.CoverEntry!.Image);
    }

    [Fact]
    public void AnAlternateVersion_PrefersTheBaseOfTheSameLanguage()
    {
        var top = """
            {"items":[
              {"regionTag":"EU_EN_fooban","tname":"Foo Ban","image":7},
              {"regionTag":"EU_JP_fooban","tname":"フーバン","image":9}
            ]}
            """;
        WriteConfig(AlternateProfile, top);

        var catalog = RomCatalog.Load(root);

        Assert.Equal(7, catalog.Find("us_us_foo_alt.bin")!.CoverEntry!.Image);
        Assert.Equal(9, catalog.Find("jp_jp_foo_alt.bin")!.CoverEntry!.Image);
    }

    [Fact]
    public void ARomWhoseTagDiffersInMoreThanATwo_GetsNoCover()
    {
        WriteConfig(AlternateProfile, AlternateTop);

        Assert.Null(RomCatalog.Load(root).Find("us_us_other.bin")!.CoverEntry);
    }

    [Fact]
    public void AFolderWithoutTheTitleTables_GivesAnEmptyCatalog()
    {
        Directory.CreateDirectory(Path.Combine(root, "032", "config"));

        var catalog = RomCatalog.Load(root);

        Assert.Equal(0, catalog.Count);
        Assert.Equal(string.Empty, catalog.RegionFolder);
        Assert.Null(catalog.Find("anything.bin"));
    }

    [Fact]
    public void BrokenJson_GivesAnEmptyCatalog()
    {
        WriteConfig("{ not json", Top);

        Assert.Equal(0, RomCatalog.Load(root).Count);
    }

    [Theory]
    [InlineData("eu", "eu")]
    [InlineData("US", "us")]
    [InlineData("jp", "jp")]
    [InlineData("xx", "")]
    public void RegionFlags(string region, string expected) =>
        Assert.Equal(expected, RomFlags.ForRegion(region));

    [Theory]
    [InlineData("en", "gb")]
    [InlineData("us", "us")]
    [InlineData("jp", "jp")]
    [InlineData("de", "de")]
    [InlineData("fr", "fr")]
    [InlineData("es", "es")]
    [InlineData("ko", "kr")]
    [InlineData("ch", "cn")]
    [InlineData("eu", "eu")]
    [InlineData("xx", "")]
    public void LanguageFlags(string language, string expected) =>
        Assert.Equal(expected, RomFlags.ForLanguage(language));

    [Fact]
    public void Prettify_CapitalisesWordsAndKeepsNumbers()
    {
        Assert.Equal("After Burner 2", RomCatalog.Prettify("jp_jp_after_burner_2.bin"));
        Assert.Equal("Dr Robotnik's Mean Bean Machine", RomCatalog.Prettify("us_us_dr_robotnik_s_mean_bean_machine.bin"));
    }

    [FirmwareFact]
    public void TheMini1Firmware_HasNamesAndCoversForAlmostEveryRom()
    {
        var tree = TestSupport.ReferenceFolder();
        var catalog = RomCatalog.Load(tree);
        var artwork = new RomArtwork(catalog.RegionFolder);

        Assert.Equal("Alex Kidd in the Enchanted Castle", catalog.Find("eu_en_Alex_Kidd.bin")!.Title);
        Assert.Equal(["eu", "jp", "us"], artwork.AvailableSets);

        var roms = RomLibrary.Scan(tree);
        var withCover = roms.Count(r =>
            catalog.Find(r.FileName)?.CoverEntry is { } entry &&
            artwork.Get(entry, CoverKind.Cover, artwork.SetForRegion(r.Region)) is not null);

        Assert.True(withCover >= 75, $"only {withCover} of {roms.Count} ROMs have a cover");

        var landstalker = catalog.Find("eu_en_Landstalker.bin")!.CoverEntry!;
        Assert.NotNull(artwork.Get(landstalker, CoverKind.Cover, "eu"));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
