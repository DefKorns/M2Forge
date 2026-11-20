// SPDX-License-Identifier: GPL-3.0-or-later
/*
 * GMWare.M2.Pecd: Library for manipulating M2 CD images used in M2 mini consoles
 * Copyright (C) 2020, 2022  Yukai Li
 * 
 * This file is part of GMWare.M2.Pecd.
 * 
 * GMWare.M2.Pecd is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * GMWare.M2.Pecd is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with GMWare.M2.Pecd.  If not, see <https://www.gnu.org/licenses/>.
 */
using Concentus.Structs;
using GMWare.M2.Pecd.Sections;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace GMWare.M2.Pecd
{
    /// <summary>
    /// Converts PECD image to CUE/ISO format.
    /// </summary>
    public class PecdToIsoConverter : IDisposable
    {
        const int OPUS_FRAME_SIZE = 5760;
        const int OPUS_SEEK_PRESKIP = 3840;
        internal const int TARGET_SAMPLE_RATE = 44100;
        string basePath;
        string discBasePath;
        int discIndex;
        StreamWriter cueWriter;
        OpusDecoder opusDecoder;
        AudzSection audz;
        InfoSection info;
        OffsSection offs;
        int dataBaseOffset;
        int discBaseOffset;
        int prevTrackLsn;
        bool warnedShortSilence;
        bool disposed;

        /// <summary>
        /// Gets the loaded archive file.
        /// </summary>
        public CdArchive Archive { get; }
        /// <summary>
        /// Gets or sets whether to write the audio past the track length for stubbed tracks.
        /// </summary>
        public bool WriteEmptySpace { get; set; }

        /// <summary>
        /// Instantiates a new instance of <see cref="PecdToIsoConverter"/>.
        /// </summary>
        /// <param name="pecdPath">The path to the PECD image.</param>
        /// <param name="basePath">The base path to extract to. i.e. file extensions and name of this path is manipulated.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="pecdPath"/> or <paramref name="basePath"/> is <c>null</c> or whitespaces.
        /// </exception>
        public PecdToIsoConverter(string pecdPath, string basePath)
        {
            if (string.IsNullOrWhiteSpace(pecdPath)) throw new ArgumentNullException(nameof(pecdPath));
            if (string.IsNullOrWhiteSpace(basePath)) throw new ArgumentNullException(nameof(basePath));
            Archive = CdArchive.OpenReadDetect(pecdPath);
            this.basePath = basePath;
            opusDecoder = new OpusDecoder(CdArchive.OPUS_SAMPLE_RATE, CdArchive.OPUS_CHANNELS);

            audz = Archive.Sections[AudzSection.TAG] as AudzSection;
            offs = Archive.Sections[OffsSection.TAG] as OffsSection;
        }

        /// <summary>
        /// Converts PECD image to CUE/ISO image.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if instance has been disposed.</exception>
        public async Task Convert()
        {
            CheckDisposed();
            dataBaseOffset = 0;
            discBaseOffset = 0;
            prevTrackLsn = 0;

            for (discIndex = 0; discIndex < Archive.DiscInfos.Count; ++discIndex)
            {
                info = Archive.DiscInfos[discIndex];
                if (Archive.DiscInfos.Count == 1)
                    discBasePath = basePath;
                else
                    discBasePath = $"{basePath}_disc_{discIndex + 1}";

                Directory.CreateDirectory(Path.GetDirectoryName(discBasePath));

                using (cueWriter = File.CreateText(discBasePath + ".cue"))
                {
                    bool lastTrackWasAudio = true;
                    for (int i = 0; i < info.Tracks.Count; ++i)
                    {
                        Console.WriteLine($"Writing disc {discIndex + 1} of {Archive.DiscInfos.Count}, track {i + 1:d2} of {info.Tracks.Count:d2}");
                        Track thisTrack = info.Tracks[i];
                        Track nextTrack = (i == info.Tracks.Count - 1) ? null : info.Tracks[i + 1];

                        // Sanity checks
                        if (thisTrack.Index00Length + thisTrack.Index01Length > 450000)
                            throw new InvalidDataException("Track length is unexpected long, table of contents may be invalid.");
                        if (thisTrack.Offset + discBaseOffset < prevTrackLsn)
                            throw new InvalidDataException("Track offset is before previous track, table of contents may be invalid.");

                        if (thisTrack.IsAudio)
                            await WriteAudioTrack(i, thisTrack, nextTrack, lastTrackWasAudio);
                        else
                            WriteDataTrack(i, thisTrack, nextTrack);
                        lastTrackWasAudio = thisTrack.IsAudio;
                        prevTrackLsn = thisTrack.Offset + discBaseOffset;
                    }
                }

                dataBaseOffset += info.DataLength;
                discBaseOffset += info.DiscLength;
            }
        }

        async Task WriteAudioTrack(int index, Track thisTrack, Track nextTrack, bool writeIndex00)
        {
            string path = $"{index + 1:d2} {Path.GetFileName(discBasePath)}.wav";
            cueWriter.WriteLine($"FILE \"{path}\" WAVE");
            cueWriter.WriteLine($"  TRACK {index + 1:d2} AUDIO");
            if (!Archive.SupportsPregap && index > 0)
            {
                cueWriter.WriteLine("    INDEX 00 00:00:00");
                cueWriter.WriteLine($"    INDEX 01 {Utils.LsnToMsf(Archive.PregapAdjustLength)}");
            }
            else
            {
                if (thisTrack.Index00Length > 0)
                {
                    if (writeIndex00)
                    {
                        cueWriter.WriteLine("    INDEX 00 00:00:00");
                        cueWriter.WriteLine($"    INDEX 01 {Utils.LsnToMsf(thisTrack.Index00Length)}");
                    }
                    else
                    {
                        cueWriter.WriteLine($"    PREGAP {Utils.LsnToMsf(thisTrack.Index00Length)}");
                        cueWriter.WriteLine("    INDEX 01 00:00:00");
                    }
                }
                else
                {
                    cueWriter.WriteLine("    INDEX 01 00:00:00");
                }
            }

            using (FileStream fs = File.Create(Path.Combine(Path.GetDirectoryName(discBasePath), path)))
            {
                int startLsn = thisTrack.Offset - (writeIndex00 ? thisTrack.Index00Length : 0);
                long startSample = (long)startLsn * CdArchive.OPUS_SAMPLE_RATE / Utils.FRAMES_PER_SECOND;
                long endSample = WriteEmptySpace ?
                    (nextTrack != null ?
                    (long)(nextTrack.Offset - nextTrack.Index00Length) * CdArchive.OPUS_SAMPLE_RATE / Utils.FRAMES_PER_SECOND :
                    audz.TotalSamples - audz.PaddingSamples) :
                    (((long)thisTrack.Offset + thisTrack.Index01Length) * CdArchive.OPUS_SAMPLE_RATE / Utils.FRAMES_PER_SECOND);
                // Fixup missing final two seconds of audio per MECD ToC
                if (Archive is MecdArchive && !WriteEmptySpace && nextTrack == null && discIndex == Archive.DiscInfos.Count - 1 &&
                    audz.TotalSamples - endSample >= 2 * CdArchive.OPUS_SAMPLE_RATE)
                    endSample += 2 * CdArchive.OPUS_SAMPLE_RATE;

                await WriteWave(fs, startSample, endSample, discBaseOffset);
            }
        }

        async Task WriteWave(Stream fs, long startSample, long endSample, int lsnOffset)
        {
            fs.SetLength(44); // Size of WAV header
            fs.Seek(0, SeekOrigin.End);

            long samplesRemaining = endSample - startSample;
            int seekPreskip = (int)Math.Min(OPUS_SEEK_PRESKIP, startSample);
            // Decoding starts after applying pre-skip:
            // - Seek pre-skip: standard suggests amount when seeking for complete convergence
            // - Encoder pre-skip: empty samples added by encoder, is a global offset
            long baseOffset = (long)lsnOffset * CdArchive.OPUS_SAMPLE_RATE / Utils.FRAMES_PER_SECOND;
            long currSamplePos = baseOffset + startSample + audz.PaddingSamples;
            int samplesWritten;

            using (SampleRateConverter src = new SampleRateConverter(CdArchive.OPUS_SAMPLE_RATE, TARGET_SAMPLE_RATE, CdArchive.OPUS_CHANNELS, true, false))
            {
                Task srcTask = src.ProcessAsync();
                Task writeTask = Utils.CopyPipelineToStream(src.Output, fs);

                // Position calculated for chunk index doesn't include encoder pre-skip because this should be in the middle of a frame
                int chunkIndex = (int)((baseOffset + startSample - seekPreskip) / CdArchive.OPUS_SAMPLE_RATE);
                long chunkSamplePos = (long)chunkIndex * CdArchive.OPUS_SAMPLE_RATE;

                while (samplesRemaining > 0)
                {
                    byte[] chunk = Archive.GetChunk(true, chunkIndex);
                    float[] decoded = DecodeAudioChunk(chunk, out int samplesDecoded);
                    Trace.Assert(samplesDecoded > 0);
                    int decodedSampleOffset = (int)(currSamplePos - chunkSamplePos);
                    if (decodedSampleOffset < samplesDecoded)
                    {
                        int copyCount = (int)Math.Min(samplesRemaining, samplesDecoded - decodedSampleOffset);
                        await src.Input.WriteAsync(Utils.CastToBytes(decoded.AsMemory(decodedSampleOffset * CdArchive.OPUS_CHANNELS, copyCount * CdArchive.OPUS_CHANNELS)));
                        samplesRemaining -= copyCount;
                        currSamplePos += copyCount;
                    }
                    ++chunkIndex;
                    chunkSamplePos += CdArchive.OPUS_SAMPLE_RATE;
                }

                src.Input.Complete();
                await srcTask;
                samplesWritten = src.SamplesWritten;
                await writeTask;
            }

            // Write WAV header
            fs.Seek(0, SeekOrigin.Begin);
            var wav = new Core.WavFile()
            {
                samplesPerSecond = TARGET_SAMPLE_RATE,
                samplesTotalCount = samplesWritten
            };
            BinaryWriter bw = new BinaryWriter(fs);
            wav.WriteHeader(bw);
        }

        void WriteDataTrack(int index, Track thisTrack, Track nextTrack)
        {
            string path = $"{index + 1:d2} {Path.GetFileName(discBasePath)}.iso";
            using (FileStream fs = File.Create(Path.Combine(Path.GetDirectoryName(discBasePath), path)))
            {
                cueWriter.WriteLine($"FILE \"{path}\" BINARY");
                cueWriter.WriteLine($"  TRACK {index + 1:d2} MODE1/2048");
                if (thisTrack.Index00Length > 0)
                    cueWriter.WriteLine($"    PREGAP {Utils.LsnToMsf(thisTrack.Index00Length)}");
                cueWriter.WriteLine("    INDEX 01 00:00:00");

                int startLsn = thisTrack.Offset;
                int endLsn = WriteEmptySpace ?
                    (nextTrack != null ?
                    nextTrack.Offset - nextTrack.Index00Length :
                    startLsn + thisTrack.Index01Length) :
                    startLsn + thisTrack.Index01Length;
                int realSectorsLeft = thisTrack.Index01Length;
                int blankSectorsLeft = endLsn - startLsn - realSectorsLeft;
                int startChunkLsn = CalcChunkLsn(startLsn);
                int chunkIndex = startChunkLsn / offs.SectorsPerChunk;

                // Write sectors in the middle of a chunk
                if (startChunkLsn % offs.SectorsPerChunk > 0)
                {
                    byte[] chunk = Archive.GetDataChunk(chunkIndex++);
                    int chunkOffset = startChunkLsn % offs.SectorsPerChunk * Utils.PECD_BYTES_PER_SECTOR;
                    int sectorsForThisTrackInChunk = (chunk.Length - chunkOffset) / Utils.PECD_BYTES_PER_SECTOR;
                    sectorsForThisTrackInChunk = Math.Min(sectorsForThisTrackInChunk, realSectorsLeft);
                    fs.Write(chunk, chunkOffset, sectorsForThisTrackInChunk * Utils.PECD_BYTES_PER_SECTOR);
                    realSectorsLeft -= sectorsForThisTrackInChunk;
                }

                // Copy chunks
                while (realSectorsLeft > offs.SectorsPerChunk)
                {
                    byte[] chunk = Archive.GetDataChunk(chunkIndex++);
                    fs.Write(chunk, 0, chunk.Length);
                    realSectorsLeft -= offs.SectorsPerChunk;
                }

                // Write leftover real chunks
                if (realSectorsLeft > 0)
                {
                    byte[] chunk = Archive.GetDataChunk(chunkIndex++);
                    fs.Write(chunk, 0, realSectorsLeft * Utils.PECD_BYTES_PER_SECTOR);
                }

                // Write remaining blank chunks for rest of the track
                if (blankSectorsLeft > 0)
                {
                    byte[] chunk = new byte[Utils.PECD_BYTES_PER_SECTOR];
                    while (blankSectorsLeft > 0)
                    {
                        fs.Write(chunk, 0, chunk.Length);
                        --blankSectorsLeft;
                    }
                }
            }
        }

        int CalcChunkLsn(int lsn)
        {
            int startIndex = dataBaseOffset;
            foreach (var track in info.Tracks)
            {
                if (track.IsAudio) continue;

                if (track.Offset + track.Index01Length < lsn)
                {
                    startIndex += track.Index01Length;
                }
                else
                {
                    startIndex += lsn - track.Offset;
                    break;
                }
            }
            return startIndex;
        }

        float[] DecodeAudioChunk(byte[] chunk, out int samplesDecoded)
        {
            samplesDecoded = 0;
            bool isSilence = chunk.Length == 0;
            if (isSilence) chunk = audz.Silence;

            using (MemoryStream ms = new MemoryStream(chunk))
            {
                BinaryReader br = new BinaryReader(ms);
                var lengths = LengthArrayEncoder.Decode(br);
                // Assume we have exactly 1 second of audio
                float[] decoded = new float[CdArchive.OPUS_SAMPLE_RATE * CdArchive.OPUS_CHANNELS];
                int i = 0;
                foreach (var length in lengths)
                {
                    byte[] packet = br.ReadBytes(length);
                    int numDecoded = opusDecoder.Decode(packet, 0, packet.Length, decoded, i, OPUS_FRAME_SIZE);
                    samplesDecoded += numDecoded;
                    i += CdArchive.OPUS_CHANNELS * numDecoded;
                }
                if (!isSilence)
                {
                    Array.Resize(ref decoded, i);
                }
                else if (samplesDecoded < CdArchive.OPUS_SAMPLE_RATE)
                {
                    if (!warnedShortSilence)
                    {
                        Console.Error.WriteLine($"Fewer samples decoded from silence than expected (got {samplesDecoded} of {CdArchive.OPUS_SAMPLE_RATE}).");
                        warnedShortSilence = true;
                    }
                    samplesDecoded = CdArchive.OPUS_SAMPLE_RATE;
                }
                return decoded;
            }
        }

        /// <summary>
        /// Disposes this instance.
        /// </summary>
        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                Archive.Dispose();
            }
        }

        void CheckDisposed()
        {
            if (disposed) throw new ObjectDisposedException(GetType().FullName);
        }
    }
}
