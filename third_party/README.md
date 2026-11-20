# Third-party code

This folder holds code from other projects, kept as it was published. Because `GMWare.M2.Pecd` is GPL-3.0-or-later, M2Forge as a whole is distributed under the GPL-3.0-or-later too (see `COPYING`).

| Folder | What it is | Licence | Source |
|---|---|---|---|
| `GMWare.M2.Pecd` | Library that reads and writes the CD image format of the M2 mini consoles (`.mcd` for the Mega Drive Mini 2, `.pcd` for the PC Engine Mini) | GPL-3.0-or-later, Copyright (C) 2020, 2022, 2024 Yukai Li. Every source file carries its own header. | "M2 CD Image Converter" (`pecd-tool`), https://gitlab.com/modmyclassic/pc-engine-mini/pecd-tool |
| `CueSharp` | Cue sheet parser | BSD 3-clause, Copyright (c) 2016 mikekov (see `CueSharp/LICENSE`) | https://gitlab.com/modmyclassic/pc-engine-mini/CueSharp |

Changes made here, none to the source files themselves:

- `GMWare.M2.Pecd/GMWare.M2.Pecd.csproj`: documentation generation off, warnings from the original code silenced, `ZstdSharp.Port` raised to the version the rest of M2Forge uses, and the `CueSharp` reference pointed at this folder.
- `CueSharp/CueSharp.csproj`: replaced by an SDK-style project targeting .NET 8 (the original targeted .NET Framework 4.6).
- Line endings: the files are stored with LF endings (the originals use CRLF); nothing else in the source files was changed.
