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
    internal class CueDataFileInfo
    {
        public enum FileType
        {
            Mode1_2048, // ISO
            Mode1_2352, // BIN
            Wav
        }

        public Stream Stream { get; set; }
        public BinaryReader BinaryReader { get; set; }
        public FileType SourceType { get; set; }
        public int WavSampleRate { get; set; }
        public int WavChannels { get; set; }
        public long WavDataStartPos { get; set; }
        public int WavTotalSamples { get; set; }

        public int GetFileMsfLength()
        {
            switch (SourceType)
            {
                case FileType.Mode1_2048:
                    return (int)(Stream.Length / 2048);
                case FileType.Mode1_2352:
                    return (int)(Stream.Length / 2352);
                case FileType.Wav:
                    // Round down LSN
                    // Before we rounded up, but that could cause out of bounds data access
                    return (int)Math.Floor((double)WavTotalSamples / WavChannels / WavSampleRate * Utils.FRAMES_PER_SECOND);
                default:
                    throw new NotSupportedException("Unknown file type");
            }
        }
    }
}
