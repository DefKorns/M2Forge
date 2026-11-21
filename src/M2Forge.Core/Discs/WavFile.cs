using System.Buffers.Binary;
using System.Text;

namespace M2Forge.Core.Discs;

public static class WavFile
{
    public static WavPcm ReadHeader(Stream stream)
    {
        Span<byte> header = stackalloc byte[12];
        stream.Position = 0;
        stream.ReadExactly(header);
        if (Encoding.ASCII.GetString(header[..4]) != "RIFF" || Encoding.ASCII.GetString(header[8..12]) != "WAVE")
            throw new InvalidDataException("Not a WAV file.");

        var format = false;
        Span<byte> chunk = stackalloc byte[8];
        Span<byte> fmt = stackalloc byte[16];
        while (stream.Position + 8 <= stream.Length)
        {
            stream.ReadExactly(chunk);
            var id = Encoding.ASCII.GetString(chunk[..4]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);

            if (id == "fmt ")
            {
                stream.ReadExactly(fmt);
                var tag = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                var channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
                var rate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]);
                var bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
                if (tag != 1 || channels != 2 || rate != 44100 || bits != 16)
                    throw new InvalidDataException("Disc audio must be 16-bit stereo PCM at 44.1 kHz.");
                format = true;
                stream.Position += size - 16 + (size & 1);
                continue;
            }

            if (id == "data")
            {
                if (!format) throw new InvalidDataException("The WAV data chunk comes before its format.");
                return new WavPcm(stream.Position, Math.Min(size, stream.Length - stream.Position));
            }

            stream.Position += size + (size & 1);
        }

        throw new InvalidDataException("The WAV file has no audio data.");
    }
}
