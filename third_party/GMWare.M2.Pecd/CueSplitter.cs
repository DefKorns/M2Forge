using CueSharp;
using GMWare.M2.Pecd.Sections;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace GMWare.M2.Pecd
{
    public class CueSplitter
    {
        Dictionary<string, CueDataFileInfo> dataFiles = new();
        List<TrackEx> tracks = new();

        public async Task Process(string cuePath, string outPath)
        {
            dataFiles.Clear();
            tracks.Clear();

            try
            {
                LoadTracksFromCue(cuePath);
                CreateTrackInfo();

                Directory.CreateDirectory(outPath);
                using StreamWriter cueWriter = File.CreateText(Path.Combine(outPath, Path.GetFileName(cuePath)));

                for (int i = 0; i < tracks.Count; ++i)
                {
                    Console.WriteLine($"Writing track {i + 1:d2} of {tracks.Count:d2}");
                    var track = tracks[i];
                    var newTrack = new CueSharp.Track(i + 1, track.IsAudio ? DataType.AUDIO : DataType.MODE1_2048);
                    string dataName = $"{i + 1:d2} {Path.GetFileNameWithoutExtension(cuePath)}";
                    if (track.IsAudio)
                    {
                        dataName += ".wav";
                        using (FileStream fs = File.Create(Path.Combine(outPath, dataName)))
                        {
                            fs.SetLength(44); // Size of WAV header
                            fs.Seek(0, SeekOrigin.End);
                            int sampleCount = track.Index01Length;
                            if (track.PregapHasAudio)
                            {
                                await track.GetPregapSubStream().CopyToAsync(fs);
                                sampleCount += track.Index00Length;
                            }

                            await track.GetSubStream().CopyToAsync(fs);

                            // Write WAV header
                            fs.Seek(0, SeekOrigin.Begin);
                            var wav = new Core.WavFile()
                            {
                                samplesPerSecond = PecdToIsoConverter.TARGET_SAMPLE_RATE,
                                samplesTotalCount = sampleCount * (PecdToIsoConverter.TARGET_SAMPLE_RATE / Utils.FRAMES_PER_SECOND)
                            };
                            BinaryWriter bw = new BinaryWriter(fs);
                            wav.WriteHeader(bw);
                        }

                        cueWriter.WriteLine($"FILE \"{dataName}\" WAVE");
                        cueWriter.WriteLine($"  TRACK {i + 1:d2} AUDIO");
                        if (track.Index00Length > 0)
                        {
                            if (track.PregapHasAudio)
                            {
                                cueWriter.WriteLine($"    INDEX 00 00:00:00");
                                cueWriter.WriteLine($"    INDEX 01 {Utils.LsnToMsf(track.Index00Length)}");
                            }
                            else
                            {
                                cueWriter.WriteLine($"    PREGAP {Utils.LsnToMsf(track.Index00Length)}");
                                cueWriter.WriteLine("    INDEX 01 00:00:00");
                            }
                        }
                        else
                        {
                            cueWriter.WriteLine("    INDEX 01 00:00:00");
                        }
                    }
                    else
                    {
                        dataName += ".iso";
                        bool isMode1_2352 = track.CueData.SourceType == CueDataFileInfo.FileType.Mode1_2352;
                        using (FileStream fs = File.Create(Path.Combine(outPath, dataName)))
                        {
                            BinaryReader br = new BinaryReader(track.GetSubStream());
                            while (br.BaseStream.Position < br.BaseStream.Length)
                            {
                                await fs.WriteAsync(track.ReadFrame(br, false));
                            }
                        }

                        cueWriter.WriteLine($"FILE \"{dataName}\" BINARY");
                        cueWriter.WriteLine($"  TRACK {i + 1:d2} MODE1/2048");
                        if (track.Index00Length > 0)
                            cueWriter.WriteLine($"    PREGAP {Utils.LsnToMsf(track.Index00Length)}");
                        cueWriter.WriteLine("    INDEX 01 00:00:00");
                    }
                }

                dataFiles.Clear();
                tracks.Clear();
            }
            catch
            {
                foreach (var dataFile in dataFiles.Values)
                {
                    dataFile.Stream.Close();
                }
                throw;
            }
        }

        void LoadTracksFromCue(string currCuePath)
        {
            CueSheet cue = new CueSheet(currCuePath);
            AudioFile lastCueDataFile = new AudioFile();
            foreach (var cueTrack in cue.Tracks.OrderBy(t => t.TrackNumber))
            {
                // Sanity checks
                // - Each track must have index 01 and none beyond that
                // - Tracks can have either index 00 or a pregap
                if (cueTrack.Indices.Length > 2)
                {
                    throw new NotSupportedException("Cannot process tracks that have more than two indexes.");
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

                if (cueTrack.TrackNumber != tracks.Count + 1)
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
                    dataFile = IsoToPecdConverter.OpenDataFile(cueTrack, Path.GetDirectoryName(currCuePath));
                    dataFiles.Add(cueTrack.DataFile.Filename, dataFile);
                }

                TrackEx track = new TrackEx
                {
                    IsAudio = cueTrack.TrackDataType == DataType.AUDIO,
                    CueTrack = cueTrack,
                    CueData = dataFile
                };

                tracks.Add(track);
            }
        }

        void CreateTrackInfo()
        {
            // 1. Determine track length (plus pregap) and offset within file
            foreach (var group in tracks.Cast<TrackEx>().GroupBy(t => t.CueData))
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
            foreach (var track in tracks)
            {
                lastMsn += track.Index00Length;
                track.Offset = lastMsn;
                lastMsn += track.Index01Length;
            }
        }
    }
}
