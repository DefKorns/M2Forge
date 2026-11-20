using System.Security.Cryptography;
using System.Text;

namespace M2Forge.Core.Firmware;

public static class PsbKeyFinder
{
    const int KeyLength = 13;

    static readonly byte[] Marker = Encoding.ASCII.GetBytes("getExistFileDirInMountArchive");
    static readonly byte[] KeyMd5 = Convert.FromHexString("c754f922596ddb680a9da4b2a0280338");

    public static string? Find(ReadOnlySpan<byte> m2engage) => FindByHash(m2engage) ?? FindByMarker(m2engage);

    public static string? FindByHash(ReadOnlySpan<byte> m2engage)
    {
        Span<byte> digest = stackalloc byte[16];
        var run = 0;
        for (var i = 0; i < m2engage.Length; i++)
        {
            run = m2engage[i] is >= 0x21 and <= 0x7E ? run + 1 : 0;
            if (run < KeyLength) continue;

            var window = m2engage.Slice(i - KeyLength + 1, KeyLength);
            MD5.HashData(window, digest);
            if (digest.SequenceEqual(KeyMd5)) return Encoding.ASCII.GetString(window);
        }
        return null;
    }

    public static string? FindByMarker(ReadOnlySpan<byte> m2engage)
    {
        var at = m2engage.IndexOf(Marker);
        if (at < 0) return null;

        var rest = m2engage[(at + Marker.Length)..];
        var start = 0;
        while (start < rest.Length && rest[start] == 0) start++;
        if (start == rest.Length) return null;

        var length = rest[start..].IndexOf((byte)0);
        if (length <= 0) return null;

        return Encoding.ASCII.GetString(rest.Slice(start, length));
    }

    public static string? FindInFile(string m2enginePath) => Find(File.ReadAllBytes(m2enginePath));
}
