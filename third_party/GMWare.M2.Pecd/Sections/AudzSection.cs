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
using System.IO;

namespace GMWare.M2.Pecd.Sections
{
    /// <summary>
    /// Represents a section for chunked audio information.
    /// </summary>
    public class AudzSection : ISection
    {
        /// <summary>
        /// The tag for the section.
        /// </summary>
        public const string TAG = "audz";

        /// <summary>
        /// Gets or sets the total number of Opus samples in the image.
        /// </summary>
        public int TotalSamples { get; set; }
        /// <summary>
        /// Gets or sets the encoder pre-skip.
        /// </summary>
        public int PaddingSamples { get; set; }
        /// <summary>
        /// Gets or sets the compressed chunk of silence.
        /// </summary>
        public byte[] Silence { get; set; }

        /// <inheritdoc/>
        public void Read(BinaryReader br, int length, Crypter crypter)
        {
            byte[] data = br.ReadBytes(length);
            crypter.Crypt(data, (uint)length);
            using (MemoryStream ms = new MemoryStream(data))
            {
                BinaryReader msbr = new BinaryReader(ms);
                TotalSamples = msbr.ReadInt32();
                PaddingSamples = msbr.ReadInt32();
                Silence = msbr.ReadBytes(length - 8);
            }
        }

        /// <inheritdoc/>
        public int Write(BinaryWriter bw, Crypter crypter)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryWriter msbw = new BinaryWriter(ms);
                msbw.Write(TotalSamples);
                msbw.Write(PaddingSamples);
                msbw.Write(Silence);
                msbw.Flush();
                byte[] data = ms.ToArray();
                crypter.Crypt(data, (uint)data.Length);
                bw.Write(data);
                return data.Length;
            }
        }
    }
}
