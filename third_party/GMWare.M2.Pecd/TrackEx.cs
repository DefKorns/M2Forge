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
using System;
using System.IO;

namespace GMWare.M2.Pecd
{
    internal class TrackEx : Track
    {
        public CueDataFileInfo CueData { get; set; }
        public int OffsetInFile { get; set; } // LSN
        public CueSharp.Track CueTrack { get; set; }
        public bool PregapHasAudio { get; set; }

        public SubStream GetSubStream()
        {
            if (CueData == null)
                throw new InvalidOperationException("No source file info");

            switch (CueData.SourceType)
            {
                case CueDataFileInfo.FileType.Mode1_2048:
                    return new SubStream(CueData.Stream, Utils.LsnToBytes(OffsetInFile, 2048), Utils.LsnToBytes(Index01Length, 2048));
                case CueDataFileInfo.FileType.Mode1_2352:
                    return new SubStream(CueData.Stream, Utils.LsnToBytes(OffsetInFile, 2352), Utils.LsnToBytes(Index01Length, 2352));
                case CueDataFileInfo.FileType.Wav:
                    int wavBytesPerFrame = CueData.WavSampleRate * CueData.WavChannels * sizeof(short) / Utils.FRAMES_PER_SECOND;
                    return new SubStream(CueData.Stream, Utils.LsnToBytes(OffsetInFile, wavBytesPerFrame) + CueData.WavDataStartPos,
                        Utils.LsnToBytes(Index01Length, wavBytesPerFrame));
                default:
                    throw new NotSupportedException("Unknown source type");
            }
        }

        public SubStream GetPregapSubStream()
        {
            if (CueData == null)
                throw new InvalidOperationException("No source file info");

            if (!PregapHasAudio && CueData.SourceType != CueDataFileInfo.FileType.Mode1_2352) return null;

            switch (CueData.SourceType)
            {
                case CueDataFileInfo.FileType.Mode1_2048:
                    return new SubStream(CueData.Stream, Utils.LsnToBytes(OffsetInFile - Index00Length, 2048), Utils.LsnToBytes(Index00Length, 2048));
                case CueDataFileInfo.FileType.Mode1_2352:
                    return new SubStream(CueData.Stream, Utils.LsnToBytes(OffsetInFile - Index00Length, 2352), Utils.LsnToBytes(Index00Length, 2352));
                case CueDataFileInfo.FileType.Wav:
                    int wavBytesPerFrame = CueData.WavSampleRate * CueData.WavChannels * sizeof(short) / Utils.FRAMES_PER_SECOND;
                    return new SubStream(CueData.Stream, Utils.LsnToBytes(OffsetInFile - Index00Length, wavBytesPerFrame) + CueData.WavDataStartPos,
                        Utils.LsnToBytes(Index00Length, wavBytesPerFrame));
                default:
                    throw new NotSupportedException("Unknown source type");
            }
        }

        public byte[] ReadFrame(BinaryReader br, bool isAudio)
        {
            if (CueData == null)
                throw new InvalidOperationException("No source file info");

            switch (CueData.SourceType)
            {
                case CueDataFileInfo.FileType.Mode1_2048:
                    return br.ReadBytes(2048);
                case CueDataFileInfo.FileType.Mode1_2352:
                    if (isAudio)
                    {
                        return br.ReadBytes(2352);
                    }
                    else
                    {
                        br.BaseStream.Seek(16, SeekOrigin.Current); // Skip header
                        byte[] data = br.ReadBytes(2048);
                        br.BaseStream.Seek(280, SeekOrigin.Current); // Skip ECC
                        return data;
                    }
                case CueDataFileInfo.FileType.Wav:
                    return br.ReadBytes(CueData.WavSampleRate / Utils.FRAMES_PER_SECOND * 2);
                default:
                    throw new NotSupportedException("Unknown source type");
            }
        }
    }
}
