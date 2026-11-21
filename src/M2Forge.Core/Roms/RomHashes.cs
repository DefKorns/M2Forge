using System.IO.Hashing;

namespace M2Forge.Core.Roms;

public sealed record RomHashes(string Crc32, string Sha1);
