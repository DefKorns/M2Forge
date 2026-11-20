# M2Forge

A desktop app to extract and repack the firmware of M2-engine consoles (Sega Mega Drive Mini and Mini 2). It replaces the command-line MArchiveBatchTool workflow with one window.

## What it does

- **Firmware**: opens a firmware folder, a `.7z` NAND dump or a raw Mini 2 eMMC image. It checks the files against a SHA-256 catalog of known firmware and extracts everything to an editable tree (`alldata`, `.m` archives and `.psb` files with their `.json` and streams).
- **Textures**: browse every image in the firmware, export them as PNG and replace them.
- **ROMs**: browse the games with names, regions and covers, and export them. Cartridges can be exported as `.bin` or ZIP. Mini 2 `.mcd` discs convert to BIN/CUE, or to CHD if `chdman` is available. The Sega CD BIOS can be exported too.
- **Build**: detects every file you changed in the extracted tree and writes new `alldata.bin`, `alldata.psb.m` files. Only changed entries are recompressed.

M2Forge only reads and writes files on your PC. It never talks to a console.

## Requirements

- Windows 10 or later.
- .NET 8 SDK to build from source.
- Optional: `chdman` (from MAME) for CHD output. Pick it in the ROMs page, or put it next to the app, in a `tools` folder or on `PATH`.

You must supply your own firmware. Nothing from the consoles is included.

## Build and test

```
dotnet build M2Forge.sln -c Release
dotnet run --project src/M2Forge.App
dotnet test
```

Most tests use synthetic data. The tests that need real firmware are skipped unless you set these variables:

| Variable | Points to |
| --- | --- |
| `M2FORGE_TEST_FW` | a folder with `alldata.bin`, `alldata.psb.m` and `m2engage` (Mega Drive Mini) |
| `M2FORGE_TEST_NAND` | a folder with the firmware `.7z` archives |
| `M2FORGE_TEST_MD2_IMAGE` | a Mini 2 eMMC image |
| `M2FORGE_TEST_MCD` | a Mini 2 `.mcd` disc |
| `M2FORGE_TEST_CHDMAN` | `chdman.exe` |
| `M2FORGE_TEST_REFERENCE` | a tree extracted by MArchiveBatchTool `fullunpack` |
| `M2FORGE_TEST_BATCH_EXE` | a MArchiveBatchTool executable |

## Layout

- `src/M2Forge.Core`: firmware, PSB, texture, ROM and disc code with no UI.
- `src/M2Forge.App`: the Avalonia app (MVVM).
- `tests/M2Forge.Tests`: xUnit tests.
- `third_party`: vendored code with its own licences, see `third_party/README.md`.

## Licence

GPL-3.0-or-later, see `COPYING`.
