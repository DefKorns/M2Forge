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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GMWare.M2.Pecd.Sections
{
    /// <summary>
    /// Represents a section for track listing and encryption key.
    /// </summary>
    public class InfoSection : ISection
    {
        /// <summary>
        /// The tag for the section.
        /// </summary>
        public const string TAG = "info";

        /// <summary>
        /// Gets or sets the list of tracks.
        /// </summary>
        public List<Track> Tracks { get; set; } = new List<Track>();
        /// <summary>
        /// Gets or sets whether this instance contains the archive key.
        /// </summary>
        public bool IsKeySource { get; set; }
        /// <summary>
        /// Gets the LSN length of all the data tracks in this disc.
        /// </summary>
        public int DataLength => Tracks.Where(t => !t.IsAudio).Sum(t => t.Index01Length);
        /// <summary>
        /// Gets the LSN length of this disc.
        /// </summary>
        public int DiscLength
        {
            get
            {
                if (Tracks.Count == 0) return 0;
                var lastTrack = Tracks[Tracks.Count - 1];
                return lastTrack.Offset + lastTrack.Index01Length;
            }
        }

        /// <inheritdoc/>
        public void Read(BinaryReader br, int length, Crypter crypter)
        {
            byte[] data = br.ReadBytes(length);
            int trackCount;
            if (crypter.ArchKey == 0)
            {
                trackCount = BitConverter.ToInt32(data, 0);
                if (trackCount > 99 || 4 * (trackCount * 3 + 1) != length)
                {
                    // Encrypted, set crypter key
                    crypter.ArchKey = BitConverter.ToUInt32(data, length - 4);
                    crypter.Crypt(data, (uint)length);
                    // Double check data
                    trackCount = BitConverter.ToInt32(data, 0);
                    if (trackCount > 99 || 4 * (trackCount * 3 + 1) != length - 4)
                        throw new InvalidDataException("Invalid info section");
                    IsKeySource = true;
                }
            }
            else
            {
                crypter.Crypt(data, (uint)length);
                trackCount = BitConverter.ToInt32(data, 0);
                if (trackCount > 99 || 4 * (trackCount * 3 + 1) != length - 4)
                    throw new InvalidDataException("Invalid info section");
            }

            using (MemoryStream ms = new MemoryStream(data))
            using (BinaryReader msbr = new BinaryReader(ms))
            {
                trackCount = msbr.ReadInt32();
                Tracks = new List<Track>();
                for (int i = 0; i < trackCount; ++i)
                {
                    Track t = new Track();
                    t.Index00Length = msbr.ReadInt32();
                    t.IsAudio = (t.Index00Length & 0x80000000) != 0;
                    t.Index00Length &= 0x7fffffff;
                    t.Offset = msbr.ReadInt32();
                    t.Index01Length = msbr.ReadInt32();
                    Tracks.Add(t);
                }
            }
        }

        /// <inheritdoc/>
        public int Write(BinaryWriter bw, Crypter crypter)
        {
            int sectionLength = 4 * (Tracks.Count * 3 + 1);
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryWriter msbw = new BinaryWriter(ms);
                msbw.Write(Tracks.Count);
                foreach (var track in Tracks)
                {
                    msbw.Write((uint)track.Index00Length | (track.IsAudio ? 0x80000000 : 0));
                    msbw.Write(track.Offset);
                    msbw.Write(track.Index01Length);
                }
                msbw.Flush();

                byte[] data = ms.ToArray();
                if (crypter.ArchKey != 0)
                {
                    sectionLength += 4;
                    crypter.Crypt(data, (uint)sectionLength);
                }
                bw.Write(data);
            }

            if (IsKeySource && crypter.ArchKey != 0)
                bw.Write(crypter.ArchKey);
            return sectionLength;
        }
    }
}
