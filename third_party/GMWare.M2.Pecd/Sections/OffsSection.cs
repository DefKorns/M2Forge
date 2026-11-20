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
using System.Collections.Generic;
using System.IO;

namespace GMWare.M2.Pecd.Sections
{
    /// <summary>
    /// Represents an offsets section.
    /// </summary>
    public class OffsSection : ISection
    {
        /// <summary>
        /// The tag for the section.
        /// </summary>
        public const string TAG = "offs";

        /// <summary>
        /// Gets or sets the total number of data sectors in the image.
        /// </summary>
        public int TotalDataSectors { get; set; }
        /// <summary>
        /// Gets or sets the number of sectors per data chunk.
        /// </summary>
        public int SectorsPerChunk { get; set; }
        /// <summary>
        /// Gets or sets the number of data chunks in the image.
        /// </summary>
        public int DataChunksCount { get; set; }
        /// <summary>
        /// Gets or sets the number of audio chunks in the image.
        /// </summary>
        public int AudioChunksCount { get; set; }
        /// <summary>
        /// Gets or sets the list of offsets.
        /// </summary>
        /// <remarks>The last entry of the list is the length of the file, used for calculating chunk lengths.</remarks>
        public List<int> Offsets { get; set; } = new List<int>();

        /// <inheritdoc/>
        public void Read(BinaryReader br, int length, Crypter crypter)
        {
            byte[] data = br.ReadBytes(length);
            crypter.Crypt(data, (uint)length);
            using (MemoryStream ms = new MemoryStream(data))
            {
                BinaryReader msbr = new BinaryReader(ms);
                TotalDataSectors = msbr.ReadInt32();
                SectorsPerChunk = msbr.ReadInt32();
                DataChunksCount = msbr.ReadInt32();
                AudioChunksCount = msbr.ReadInt32();
                Offsets = new List<int>();
                for (int i = 0; i < DataChunksCount + AudioChunksCount + 1; ++i)
                {
                    Offsets.Add(msbr.ReadInt32());
                }
            }
        }

        /// <inheritdoc/>
        public int Write(BinaryWriter bw, Crypter crypter)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryWriter msbw = new BinaryWriter(ms);
                msbw.Write(TotalDataSectors);
                msbw.Write(SectorsPerChunk);
                msbw.Write(DataChunksCount);
                msbw.Write(AudioChunksCount);
                foreach (var offset in Offsets)
                {
                    msbw.Write(offset);
                }
                msbw.Flush();
                byte[] data = ms.ToArray();
                crypter.Crypt(data, (uint)data.Length);
                bw.Write(data);
                return data.Length;
            }
        }
    }
}
