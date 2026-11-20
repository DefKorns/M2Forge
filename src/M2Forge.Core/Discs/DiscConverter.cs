using System.Text;
using GMWare.M2.Pecd;

namespace M2Forge.Core.Discs;

public sealed record DiscImage(string CuePath, string BinPath);

public static class DiscConverter
{
    public static async Task<IReadOnlyList<DiscImage>> ConvertAsync(string mcdPath, string outputBase, CancellationToken cancellation = default)
    {
        var outputFolder = Path.GetDirectoryName(Path.GetFullPath(outputBase))!;
        Directory.CreateDirectory(outputFolder);
        var work = Path.Combine(outputFolder, ".m2forge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        try
        {
            var name = Path.GetFileName(outputBase);
            using (var converter = new PecdToIsoConverter(mcdPath, Path.Combine(work, name)))
            {
                await Task.Run(() => converter.Convert(), cancellation).ConfigureAwait(false);
            }

            var images = new List<DiscImage>();
            foreach (var cue in Directory.GetFiles(work, "*.cue").Order(StringComparer.Ordinal))
            {
                cancellation.ThrowIfCancellationRequested();
                var stem = Path.GetFileNameWithoutExtension(cue);
                var bin = Path.Combine(outputFolder, stem + ".bin");
                var cueOut = Path.Combine(outputFolder, stem + ".cue");
                var tracks = CueSheet.Parse(await File.ReadAllLinesAsync(cue, cancellation).ConfigureAwait(false));

                var tempBin = bin + ".part";
                var sheet = await Task.Run(() => WriteBin(tracks, work, tempBin, stem + ".bin", cancellation), cancellation).ConfigureAwait(false);
                File.Move(tempBin, bin, overwrite: true);
                await File.WriteAllTextAsync(cueOut, sheet, new UTF8Encoding(false), cancellation).ConfigureAwait(false);
                images.Add(new DiscImage(cueOut, bin));
            }
            return images;
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    static string WriteBin(IReadOnlyList<CueTrack> tracks, string work, string binPath, string binName, CancellationToken cancellation)
    {
        var cue = new StringBuilder();
        cue.Append("FILE \"").Append(binName).Append("\" BINARY\n");

        using var bin = new FileStream(binPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        var silence = new byte[CdSector.RawSize];
        var frame = 0;

        foreach (var track in tracks)
        {
            cancellation.ThrowIfCancellationRequested();
            var source = Path.Combine(work, track.File);
            var start = frame;
            var index00 = (int?)null;

            if (track.Pregap > 0 && track.Number > 1)
            {
                for (var i = 0; i < track.Pregap; i++) bin.Write(silence);
                frame += track.Pregap;
                index00 = start;
            }
            else if (track.Index00 is { } relative00)
            {
                index00 = start + relative00;
            }

            var index01 = track.Index00 is not null
                ? start + track.Index01
                : frame + (track.Pregap > 0 && track.Number > 1 ? 0 : track.Index01);

            cue.Append("  TRACK ").Append(track.Number.ToString("00")).Append(track.IsAudio ? " AUDIO\n" : " MODE1/2352\n");
            if (index00 is { } i00) cue.Append("    INDEX 00 ").Append(CueSheet.Msf(i00)).Append('\n');
            cue.Append("    INDEX 01 ").Append(CueSheet.Msf(index01)).Append('\n');

            frame += track.IsAudio ? CopyAudio(source, bin) : CopyData(source, bin, frame);
        }

        return cue.ToString();
    }

    static int CopyData(string isoPath, Stream bin, int firstFrame)
    {
        using var iso = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        var data = new byte[CdSector.DataSize];
        var sector = new byte[CdSector.RawSize];
        var count = 0;
        while (iso.Read(data, 0, data.Length) is var read and > 0)
        {
            if (read < data.Length) Array.Clear(data, read, data.Length - read);
            CdSector.WriteMode1(data, firstFrame + count, sector);
            bin.Write(sector);
            count++;
        }
        return count;
    }

    static int CopyAudio(string wavPath, Stream bin)
    {
        using var wav = new FileStream(wavPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        var pcm = WavFile.ReadHeader(wav);
        var buffer = new byte[CdSector.RawSize * 64];
        var remaining = pcm.DataLength;
        wav.Position = pcm.DataOffset;
        long written = 0;
        while (remaining > 0)
        {
            var read = wav.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0) break;
            bin.Write(buffer, 0, read);
            written += read;
            remaining -= read;
        }

        var tail = (int)(written % CdSector.RawSize);
        if (tail != 0)
        {
            bin.Write(new byte[CdSector.RawSize - tail]);
            written += CdSector.RawSize - tail;
        }
        return (int)(written / CdSector.RawSize);
    }
}
