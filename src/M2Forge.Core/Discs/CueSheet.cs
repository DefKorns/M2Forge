using System.Globalization;
using System.Text.RegularExpressions;

namespace M2Forge.Core.Discs;

public sealed record CueTrack(int Number, bool IsAudio, string File, int Pregap, int? Index00, int Index01);

public static partial class CueSheet
{
    public static IReadOnlyList<CueTrack> Parse(IEnumerable<string> lines)
    {
        var tracks = new List<CueTrack>();
        string? file = null;
        var number = 0;
        var audio = false;
        var pregap = 0;
        int? index00 = null;
        int? index01 = null;
        var open = false;

        void Close()
        {
            if (!open) return;
            if (file is null || index01 is null) throw new InvalidDataException($"Track {number} has no file or no INDEX 01.");
            tracks.Add(new CueTrack(number, audio, file, pregap, index00, index01.Value));
            open = false;
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (FileLine().Match(line) is { Success: true } f)
            {
                Close();
                file = f.Groups[1].Value;
            }
            else if (TrackLine().Match(line) is { Success: true } t)
            {
                Close();
                number = int.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture);
                audio = t.Groups[2].Value.Equals("AUDIO", StringComparison.OrdinalIgnoreCase);
                pregap = 0;
                index00 = null;
                index01 = null;
                open = true;
            }
            else if (open && PregapLine().Match(line) is { Success: true } p)
            {
                pregap = ToFrames(p.Groups[1].Value);
            }
            else if (open && IndexLine().Match(line) is { Success: true } i)
            {
                var frames = ToFrames(i.Groups[2].Value);
                if (i.Groups[1].Value == "00") index00 = frames;
                else if (i.Groups[1].Value == "01") index01 = frames;
            }
        }
        Close();
        return tracks;
    }

    public static string Msf(int frames) =>
        string.Create(CultureInfo.InvariantCulture, $"{frames / (75 * 60):00}:{frames / 75 % 60:00}:{frames % 75:00}");

    public static int ToFrames(string msf)
    {
        var parts = msf.Split(':');
        return int.Parse(parts[0], CultureInfo.InvariantCulture) * 75 * 60 +
               int.Parse(parts[1], CultureInfo.InvariantCulture) * 75 +
               int.Parse(parts[2], CultureInfo.InvariantCulture);
    }

    [GeneratedRegex("^FILE\\s+\"(.+)\"\\s+\\w+$", RegexOptions.IgnoreCase)]
    private static partial Regex FileLine();

    [GeneratedRegex("^TRACK\\s+(\\d+)\\s+(\\S+)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrackLine();

    [GeneratedRegex("^PREGAP\\s+(\\d+:\\d+:\\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PregapLine();

    [GeneratedRegex("^INDEX\\s+(\\d+)\\s+(\\d+:\\d+:\\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex IndexLine();
}
