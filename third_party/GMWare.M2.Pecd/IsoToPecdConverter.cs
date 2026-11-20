// SPDX-License-Identifier: GPL-3.0-or-later
/*
 * GMWare.M2.Pecd: Library for manipulating M2 CD images used in M2 mini consoles
 * Copyright (C) 2020, 2022, 2024  Yukai Li
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
using CueSharp;
using GMWare.M2.Pecd.Sections;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Threading.Tasks;

namespace GMWare.M2.Pecd
{
    /// <summary>
    /// Converts cue sheet-based CD images to PECD format.
    /// </summary>
    public class IsoToPecdConverter
    {
        const int OPUS_FRAME_SIZE = 960;

        string archAlias;
        List<string> cuePaths;
        int discIndex;
        string currCuePath;
        string targetPath;
        string tmpDataPath;
        string tmpAudioPath;
        Stream tmpDataStream;
        Stream tmpAudioStream;
        string biosPath;
        string bios2Path;
        AudioChunkGenerator audChunkGen;
        CdArchive archive;
        Dictionary<string, CueDataFileInfo> dataFiles;
        float[] silence;
        List<int> dataOffsets;
        int dataSectorsWritten;
        bool gutted;
        bool noSharedSilence;
        int discBaseOffset;

        InfoSection infoSection;
        OffsSection offsSection;
        AudzSection audzSection;

        /// <summary>
        /// Instantiates a new instance of <see cref="IsoToPecdConverter"/>
        /// </summary>
        /// <param name="archAlias">The alias of the CD image type.</param>
        /// <param name="cuePaths">The path to the cue sheets for each disc.</param>
        /// <param name="targetPath">The path of the output image.</param>
        /// <param name="biosPath">The path of the primary BIOS image.</param>
        /// <param name="bios2Path">The path of a secondary BIOS image.</param>
        /// <param name="gutted">If <c>true</c>, stubs out all but the first data track.</param>
        /// <param name="noSharedSilence"></param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="archAlias"/>, <paramref name="targetPath"/>, <paramref name="biosPath"/> is <c>null</c> or whitespace,
        /// or if <paramref name="cuePaths"/> is <c>null</c> or contains no items. Also thrown if <paramref name="archAlias"/> is "pecd"
        /// and <paramref name="bios2Path"/> is <c>null</c> or whitespace.
        /// </exception>
        public IsoToPecdConverter(string archAlias, IList<string> cuePaths, string targetPath, string biosPath, string bios2Path, bool gutted = false, bool noSharedSilence = false)
        {
            if (string.IsNullOrWhiteSpace(archAlias)) throw new ArgumentNullException(nameof(archAlias));
            if (cuePaths == null || cuePaths.Count == 0) throw new ArgumentNullException(nameof(cuePaths));
            if (string.IsNullOrWhiteSpace(targetPath)) throw new ArgumentNullException(nameof(targetPath));
            if (string.IsNullOrWhiteSpace(biosPath)) throw new ArgumentNullException(nameof(biosPath));
            if (archAlias == "pecd" && string.IsNullOrWhiteSpace(bios2Path)) throw new ArgumentNullException(nameof(bios2Path));
            this.archAlias = archAlias;
            this.cuePaths = new List<string>(cuePaths);
            this.targetPath = targetPath;
            this.biosPath = biosPath;
            this.bios2Path = bios2Path;
            this.gutted = gutted;
            this.noSharedSilence = noSharedSilence;
            silence = new float[CdArchive.OPUS_SAMPLE_RATE * CdArchive.OPUS_CHANNELS];
        }

        /// <summary>
        /// Converts cue sheet-based image to PECD image.
        /// </summary>
        /// <param name="encrypt">Whether to encrypt the PECD image.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task Convert(bool encrypt)
        {
            using (archive = CdArchive.InstantiateByAlias(archAlias))
            {
                archive.Sections.Add(ArchSection.TAG, new ArchSection { Value = archive.ArchString });
                offsSection = new OffsSection();
                offsSection.SectorsPerChunk = archive.DefaultSectorsPerChunk;
                archive.Sections.Add(OffsSection.TAG, offsSection);
                audzSection = new AudzSection();
                if (!noSharedSilence) audzSection.Silence = await GenerateSilence();
                archive.Sections.Add(AudzSection.TAG, audzSection);
                // Later: add bios and bio2

                tmpDataPath = targetPath + ".dat.tmp";
                tmpAudioPath = targetPath + ".aud.tmp";
                using (tmpDataStream = File.Create(tmpDataPath))
                using (tmpAudioStream = File.Create(tmpAudioPath))
                {
                    audChunkGen = new AudioChunkGenerator(CdArchive.OPUS_SAMPLE_RATE, CdArchive.OPUS_CHANNELS, tmpAudioStream, OPUS_FRAME_SIZE, noSharedSilence ? null : silence, gutted);
                    audzSection.PaddingSamples = audChunkGen.Lookahead;
                    dataOffsets = new List<int>();
                    dataSectorsWritten = 0;

                    Task audProcTask = audChunkGen.BeginProcessing();
                    if (audProcTask.IsFaulted) throw audProcTask.Exception;

                    for (discIndex = 0; discIndex < cuePaths.Count; ++discIndex)
                    {
                        currCuePath = cuePaths[discIndex];
                        infoSection = new InfoSection();
                        if (discIndex == 0) infoSection.IsKeySource = true;
                        await ProcessCueSheet();
                        archive.DiscInfos.Add(infoSection);
                        discBaseOffset += infoSection.DiscLength;
                    }

                    // Pad with pre-skip number of samples so decoder doesn't suddenly run out of samples
                    // Note: function takes number of single channel samples, so need to multiply by number of channels;
                    // Position is 0 because discBaseOffset is applied inside the function and we've already updated it
                    await WriteSilenceSamples(0, audzSection.PaddingSamples * CdArchive.OPUS_CHANNELS);

                    await audChunkGen.FinishAsync();
                    audzSection.TotalSamples = (int)audChunkGen.SamplesWritten;

                    tmpDataStream.Flush();
                    offsSection.TotalDataSectors = dataSectorsWritten;
                    offsSection.DataChunksCount = dataOffsets.Count;
                    offsSection.AudioChunksCount = audChunkGen.ChunkOffsets.Count - 1;
                    offsSection.Offsets = new List<int>(dataOffsets);
                    offsSection.Offsets.AddRange(audChunkGen.ChunkOffsets);

                    archive.Sections.Add(BiosSection.TAG, new BiosSection { Data = File.ReadAllBytes(biosPath), SkipCrypt = archive is MecdArchive });
                    if (bios2Path != null) archive.Sections.Add(Bio2Section.TAG, new Bio2Section { Data = File.ReadAllBytes(bios2Path) });

                    uint key = 0;
                    if (encrypt)
                    {
                        Random rand = new Random();
                        key = unchecked((uint)rand.Next()) | 0x4d4d43;
                    }

                    Console.WriteLine("Finalizing");
                    archive.OpenWrite(targetPath, key);

                    BinaryReader audBr = new BinaryReader(tmpAudioStream);
                    if (noSharedSilence)
                    {
                        // Pull last chunk into audz
                        int chunksCount = offsSection.Offsets.Count;
                        int lastChunkOffset = offsSection.Offsets[chunksCount - 2];
                        tmpAudioStream.Seek(lastChunkOffset, SeekOrigin.Begin);
                        byte[] chunk = audBr.ReadBytes(offsSection.Offsets[chunksCount - 1] - lastChunkOffset);
                        audzSection.Silence = chunk;
                        offsSection.Offsets[chunksCount - 1] = lastChunkOffset;
                    }

                    int chunkAdjust = archive.WriteHeaders();

                    tmpDataStream.Seek(0, SeekOrigin.Begin);
                    BinaryReader dataBr = new BinaryReader(tmpDataStream);
                    for (int i = 0; i < offsSection.DataChunksCount; ++i)
                    {
                        int nextOffset = (i == offsSection.DataChunksCount - 1) ? (int)tmpDataStream.Length : offsSection.Offsets[i + 1];
                        byte[] chunk = dataBr.ReadBytes(nextOffset - offsSection.Offsets[i]);
                        offsSection.Offsets[i] = chunkAdjust;
                        int compressedSize = archive.WriteDataChunk(chunk, i);
                        chunkAdjust += compressedSize;
                    }

                    tmpAudioStream.Seek(0, SeekOrigin.Begin);
                    for (int i = dataOffsets.Count; i < offsSection.Offsets.Count - 1; ++i)
                    {
                        byte[] chunk = audBr.ReadBytes(offsSection.Offsets[i + 1] - offsSection.Offsets[i]);
                        archive.WriteChunk(chunk, true, i - offsSection.DataChunksCount);
                        offsSection.Offsets[i] += chunkAdjust;
                    }

                    offsSection.Offsets[offsSection.Offsets.Count - 1] += chunkAdjust;

                    archive.WriteHeaders();
                }

                File.Delete(tmpDataPath);
                File.Delete(tmpAudioPath);
            }
        }

        async Task<byte[]> GenerateSilence()
        {
            using (MemoryStream ms = new MemoryStream())
            using (AudioChunkGenerator gen = new AudioChunkGenerator(CdArchive.OPUS_SAMPLE_RATE, CdArchive.OPUS_CHANNELS, ms, OPUS_FRAME_SIZE, null))
            {
                Task procTask = gen.BeginProcessing();
                Pipe pipe = new Pipe();
                Task genProcessTask = gen.ProcessAsync(0, pipe.Reader);
                await pipe.Writer.WriteAsync(Utils.CastToBytes(silence, silence.Length));
                pipe.Writer.Complete();
                await genProcessTask;
                await gen.FinishAsync();
                return ms.ToArray();
            }
        }

        async Task ProcessCueSheet()
        {
            dataFiles = new Dictionary<string, CueDataFileInfo>();
            try
            {
                LoadTracksFromCue();
                CreateTrackInfo();

                for (int i = 0; i < infoSection.Tracks.Count; ++i)
                {
                    var track = infoSection.Tracks[i];
                    Console.WriteLine($"Processing disc {discIndex + 1} of {cuePaths.Count}, track {i + 1:d2} of {infoSection.Tracks.Count:d2}");
                    await ProcessAudioTrack(track as TrackEx);
                    if (!track.IsAudio)
                        ProcessDataTrack(track as TrackEx, i);
                }

                if (!archive.SupportsPregap)
                {
                    if (gutted)
                    {
                        // Not really sure what I'm supposed to do here, so I'll just adjust everything back by 2 seconds?
                        foreach (var track in infoSection.Tracks)
                        {
                            if (track.IsAudio) track.Offset -= archive.PregapAdjustLength;
                        }
                    }
                    else
                    {
                        // Adjust each track's position so offset is always 2 seconds before index 01 and
                        // merge time into previous track's index 01 length
                        int totalLength = infoSection.DiscLength;
                        for (int i = 0; i < infoSection.Tracks.Count - 1; ++i)
                        {
                            var track = infoSection.Tracks[i];
                            var nextTrack = infoSection.Tracks[i + 1];
                            // Don't know what happens when next track is data, Mega CD tends to not have
                            // multiple data tracks. Assume we're not supposed to modify them in any way.
                            if (nextTrack.IsAudio)
                            {
                                nextTrack.Offset -= archive.PregapAdjustLength;
                                track.Index00Length = 0;
                                track.Index01Length = nextTrack.Offset - track.Offset;
                            }
                        }

                        if (infoSection.Tracks.Count > 1)
                        {
                            var lastTrack = infoSection.Tracks[infoSection.Tracks.Count - 1];
                            lastTrack.Index00Length = 0;
                            // Note that original MECDs have a bug where last track is 2 seconds shorter
                            // than they're supposed to be. We won't replicate that here.
                            lastTrack.Index01Length = totalLength - lastTrack.Offset;
                        }
                    }
                }
            }
            finally
            {
                foreach (var fileInfo in dataFiles.Values)
                    fileInfo.Stream.Close();
            }
        }

        void LoadTracksFromCue()
        {
            CueSheet cue = new CueSheet(currCuePath);
            AudioFile lastCueDataFile = new AudioFile();
            foreach (var cueTrack in cue.Tracks.OrderBy(t => t.TrackNumber))
            {
                // Sanity checks
                // - Track number valid for CD
                // - Each track must have index 01 and none beyond that
                // - Tracks can have either index 00 or a pregap
                if (cueTrack.TrackNumber < 1 || cueTrack.TrackNumber > 99)
                {
                    throw new NotSupportedException($"Track number {cueTrack.TrackNumber} is outside of range representable on a CD.");
                }
                if (cueTrack.Indices.Length > 2)
                {
                    throw new NotSupportedException($"Cannot process track {cueTrack.TrackNumber}, which has more than two indexes.");
                }
                else if (cueTrack.Indices.Length == 2)
                {
                    if (cueTrack.Indices[0].Number != 0 && cueTrack.Indices[1].Number != 1)
                        throw new NotSupportedException($"Unknown index layout on track {cueTrack.TrackNumber}.");
                }
                else if (cueTrack.Indices.Length == 1)
                {
                    if (cueTrack.Indices[0].Number != 1)
                        throw new NotSupportedException($"Unknown index layout on track {cueTrack.TrackNumber}.");
                }
                else if (cueTrack.Indices.Length == 0)
                {
                    throw new NotSupportedException($"Track {cueTrack.TrackNumber} does not contain any indexes.");
                }

                if (cueTrack.PostGap.IsIndexValid())
                    throw new NotSupportedException($"Track {cueTrack.TrackNumber} contains postgap.");

                if (cueTrack.TrackNumber != infoSection.Tracks.Count + 1)
                    throw new NotSupportedException($"Tracks contain gaps.");

                // CueSharp does not assign file to every track it applies to, so do it here
                if (lastCueDataFile.Filename == null)
                {
                    if (cueTrack.DataFile.Filename == null)
                        throw new InvalidDataException("No file for track.");

                    lastCueDataFile = cueTrack.DataFile;
                }
                else if (cueTrack.DataFile.Filename == null)
                {
                    cueTrack.DataFile = lastCueDataFile;
                }

                // Open data file
                if (!dataFiles.TryGetValue(cueTrack.DataFile.Filename, out var dataFile))
                {
                    dataFile = OpenDataFile(cueTrack, Path.GetDirectoryName(currCuePath));
                    dataFiles.Add(cueTrack.DataFile.Filename, dataFile);
                }

                TrackEx track = new TrackEx
                {
                    IsAudio = cueTrack.TrackDataType == DataType.AUDIO,
                    CueTrack = cueTrack,
                    CueData = dataFile
                };

                infoSection.Tracks.Add(track);
            }
        }

        void CreateTrackInfo()
        {
            // 1. Determine track length (plus pregap) and offset within file
            foreach (var group in infoSection.Tracks.Cast<TrackEx>().GroupBy(t => t.CueData))
            {
                var dataFile = group.Key;
                // Order tracks by time
                var orderedTracks = group.OrderBy(t => t.CueTrack.GetIndex(1).GetLsn()).ToArray();
                for (int i = 0; i < orderedTracks.Length; ++i)
                {
                    var currTrack = orderedTracks[i];
                    currTrack.OffsetInFile = currTrack.CueTrack.GetIndex(1).GetLsn();

                    // Pregap check
                    if (currTrack.CueTrack.PreGap.IsIndexValid())
                    {
                        // Just standard pregap
                        currTrack.Index00Length = currTrack.CueTrack.PreGap.GetLsn();
                    }
                    else if (currTrack.CueTrack.Indices[0].Number == 0)
                    {
                        // Pregap as index
                        currTrack.Index00Length = currTrack.CueTrack.GetIndex(1).GetLsn() - currTrack.CueTrack.GetIndex(0).GetLsn();
                        currTrack.PregapHasAudio = true;
                    }

                    // Set length
                    if (i < orderedTracks.Length - 1)
                    {
                        // Get next track to find length
                        // Pick first index in track (either pregap with audio or index 01) to calculate with
                        var nextTrack = orderedTracks[i + 1];
                        int nextTrackOffset = nextTrack.CueTrack.Indices[0].GetLsn();
                        currTrack.Index01Length = nextTrackOffset - currTrack.OffsetInFile;
                    }
                    else
                    {
                        // Use file length to find track length
                        currTrack.Index01Length = dataFile.GetFileMsfLength() - currTrack.OffsetInFile;
                    }
                }
            }

            // 2. Assign offset to each track
            int lastMsn = 0;
            foreach (var track in infoSection.Tracks)
            {
                lastMsn += track.Index00Length;
                track.Offset = lastMsn;
                lastMsn += track.Index01Length;
            }

            if (gutted)
            {
                bool sawDataTrack = false;
                TrackEx lastDataTrack = null;
                int numDataTracks = 0;
                foreach (var track in infoSection.Tracks.Cast<TrackEx>())
                {
                    if (track.IsAudio)
                    {
                        track.Index01Length = 76;
                    }
                    else
                    {
                        ++numDataTracks;
                        if (sawDataTrack)
                        {
                            track.Index01Length = 1;
                        }
                        else
                        {
                            sawDataTrack = true;
                        }
                        lastDataTrack = track;
                    }
                }
                if (numDataTracks > 1)
                {
                    lastDataTrack.Index01Length = 2;
                }
            }
        }

        internal static CueDataFileInfo OpenDataFile(CueSharp.Track cueTrack, string basePath)
        {
            if (cueTrack.DataFile.Filetype != FileType.BINARY && cueTrack.DataFile.Filetype != FileType.WAVE)
                throw new NotSupportedException($"{cueTrack.DataFile.Filetype} not supported.");

            CueDataFileInfo dataFile = new CueDataFileInfo();
            dataFile.Stream = File.OpenRead(Path.Combine(basePath, cueTrack.DataFile.Filename));
            dataFile.BinaryReader = new BinaryReader(dataFile.Stream);
            dataFile.WavChannels = 2;
            dataFile.WavSampleRate = 44100; // Default, overwritten if loading from WAV
            if (cueTrack.DataFile.Filetype == FileType.WAVE)
            {
                Core.WavFile wavFile = new Core.WavFile();
                if (!wavFile.ReadHeader(dataFile.BinaryReader))
                    throw new NotSupportedException($"Data file {cueTrack.DataFile.Filename} not supported WAV file.");

                dataFile.SourceType = CueDataFileInfo.FileType.Wav;
                dataFile.WavDataStartPos = wavFile.DataStartPos;
                dataFile.WavSampleRate = wavFile.samplesPerSecond;
                dataFile.WavTotalSamples = wavFile.samplesTotalCount;
            }
            else if (cueTrack.TrackDataType == DataType.AUDIO && cueTrack.DataFile.Filetype == FileType.BINARY)
            {
                dataFile.SourceType = CueDataFileInfo.FileType.Mode1_2352;
            }
            else if (cueTrack.TrackDataType == DataType.MODE1_2048)
            {
                dataFile.SourceType = CueDataFileInfo.FileType.Mode1_2048;
            }
            else if (cueTrack.TrackDataType == DataType.MODE1_2352)
            {
                dataFile.SourceType = CueDataFileInfo.FileType.Mode1_2352;
            }
            else
            {
                throw new NotSupportedException("Unknown data file type.");
            }

            return dataFile;
        }

        async Task ProcessAudioTrack(TrackEx track)
        {
            if (track.Index00Length > 0)
            {
                int pregapPos = track.Offset - track.Index00Length;
                if (track.PregapHasAudio)
                {
                    await WriteAudio(pregapPos, track.CueData, track.GetPregapSubStream());
                }
                else
                {
                    await WriteSilence(pregapPos, track.Index00Length);
                }
            }

            if (track.IsAudio)
            {
                await WriteAudio(track.Offset, track.CueData, track.GetSubStream());
            }
            else
            {
                // Data tracks are silent
                await WriteSilence(track.Offset, track.Index01Length);
            }
        }

        async Task WriteAudio(int lsnPosition, CueDataFileInfo dataFile, Stream stream)
        {
            if (dataFile.SourceType != CueDataFileInfo.FileType.Wav && dataFile.SourceType != CueDataFileInfo.FileType.Mode1_2352)
                throw new ArgumentException("Invalid source file type.", nameof(dataFile));
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            using (SampleRateConverter src = new SampleRateConverter(dataFile.WavSampleRate, CdArchive.OPUS_SAMPLE_RATE, CdArchive.OPUS_CHANNELS, false, true))
            {
                Task srcTask = src.ProcessAsync();
                long position = (long)(lsnPosition + discBaseOffset) * CdArchive.OPUS_SAMPLE_RATE / Utils.FRAMES_PER_SECOND;
                Task procTask = audChunkGen.ProcessAsync(position, src.Output);
                if (procTask.IsFaulted) await procTask;
                Memory<byte> buffer = new byte[4096];
                int read;
                do
                {
                    read = await stream.ReadAsync(buffer);
                    await src.Input.WriteAsync(buffer.Slice(0, read));
                } while (read > 0);
                src.Input.Complete();

                await srcTask;
                await procTask;
            }
        }

        async Task WriteSilence(int lsnPosition, int lsnLength)
        {
            long length = (long)lsnLength * CdArchive.OPUS_SAMPLE_RATE * CdArchive.OPUS_CHANNELS / Utils.FRAMES_PER_SECOND;
            await WriteSilenceSamples(lsnPosition, length);
        }

        async Task WriteSilenceSamples(int lsnPosition, long length)
        {
            long position = (long)(lsnPosition + discBaseOffset) * CdArchive.OPUS_SAMPLE_RATE / Utils.FRAMES_PER_SECOND;

            Pipe pipe = new Pipe();
            Task procTask = audChunkGen.ProcessAsync(position, pipe.Reader);

            if (length >= silence.Length)
            {
                var silenceMem = Utils.CastToBytes(silence, silence.Length);
                while (length >= silence.Length)
                {
                    await pipe.Writer.WriteAsync(silenceMem);
                    length -= silence.Length;
                }
            }

            if (length > 0)
            {
                var silenceMem = Utils.CastToBytes(silence, (int)length);
                await pipe.Writer.WriteAsync(silenceMem);
            }

            pipe.Writer.Complete();
            await procTask;
        }

        void ProcessDataTrack(TrackEx track, int trackIndex)
        {
            var dataFile = track.CueData;
            if (dataFile.SourceType != CueDataFileInfo.FileType.Mode1_2048 && dataFile.SourceType != CueDataFileInfo.FileType.Mode1_2352)
                throw new ArgumentException("Invalid source file type.", nameof(dataFile));

            var subStream = track.GetSubStream();
            BinaryReader subReader = new BinaryReader(subStream);

            int remainingSectors = track.Index01Length;
            if (!archive.SupportsPregap)
            {
                // Pregap adjustment affects data tracks too, so we need to calculate how many
                // sectors we actually want. Luckily CD standards stipulate at least 2 seconds
                // of postgap, so we shouldn't eat into actual data if next track contains less
                // than 2 seconds of pregap. Note that the track info we have here is before
                // they've been adjusted.
                if (trackIndex < infoSection.Tracks.Count - 1)
                {
                    var nextTrack = infoSection.Tracks[trackIndex + 1];
                    if (nextTrack.IsAudio)
                        remainingSectors = nextTrack.Offset - archive.PregapAdjustLength - track.Offset;
                }
            }
            while (remainingSectors > 0)
            {
                bool readFromStream = subStream.Position < subStream.Length;
                if (readFromStream && dataFile.SourceType == CueDataFileInfo.FileType.Mode1_2352)
                    subStream.Seek(16, SeekOrigin.Current);

                byte[] sector = readFromStream ? subReader.ReadBytes(Utils.PECD_BYTES_PER_SECTOR) : new byte[Utils.PECD_BYTES_PER_SECTOR];
                if (dataSectorsWritten % offsSection.SectorsPerChunk == 0)
                    dataOffsets.Add((int)tmpDataStream.Position);
                tmpDataStream.Write(sector);
                ++dataSectorsWritten;
                --remainingSectors;

                if (readFromStream && dataFile.SourceType == CueDataFileInfo.FileType.Mode1_2352)
                    subStream.Seek(288, SeekOrigin.Current);
            }
        }
    }
}
