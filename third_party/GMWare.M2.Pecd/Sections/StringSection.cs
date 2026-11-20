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
using System.Text;

namespace GMWare.M2.Pecd.Sections
{
    /// <summary>
    /// Represents a section containing string data.
    /// </summary>
    public class StringSection : ISection
    {
        /// <summary>
        /// Gets or sets the value of the section.
        /// </summary>
        public string Value { get; set; }
        /// <summary>
        /// Gets or sets whether this section should be encrypted.
        /// </summary>
        public bool Encrypt { get; set; }

        /// <inheritdoc/>
        public void Read(BinaryReader br, int length, Crypter crypter)
        {
            byte[] data = br.ReadBytes(length);
            if (Encrypt) crypter.Crypt(data, (uint)length);
            Value = Encoding.UTF8.GetString(data).TrimEnd('\0');
        }

        /// <inheritdoc/>
        public int Write(BinaryWriter bw, Crypter crypter)
        {
            byte[] data = Encoding.UTF8.GetBytes(Value + '\0');
            if (Encrypt) crypter.Crypt(data, (uint)data.Length);
            bw.Write(data);
            return data.Length;
        }
    }
}
