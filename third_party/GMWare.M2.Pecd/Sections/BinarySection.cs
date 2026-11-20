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
    /// Represents a section for storing arbitrary data.
    /// </summary>
    public class BinarySection : ISection
    {
        /// <summary>
        /// Gets or sets the stored data.
        /// </summary>
        public byte[] Data { get; set; }

        /// <summary>
        /// Gets or sets whether the section should skip being encrypted/decrypted.
        /// </summary>
        public bool SkipCrypt { get; set; }

        /// <inheritdoc/>
        public void Read(BinaryReader br, int length, Crypter crypter)
        {
            Data = br.ReadBytes(length);
            if (!SkipCrypt) crypter.Crypt(Data, (uint)length);
        }

        /// <inheritdoc/>
        public int Write(BinaryWriter bw, Crypter crypter)
        {
            byte[] cloneData = (byte[])Data.Clone();
            if (!SkipCrypt) crypter.Crypt(cloneData, (uint)cloneData.Length);
            bw.Write(cloneData);
            return cloneData.Length;
        }
    }
}
