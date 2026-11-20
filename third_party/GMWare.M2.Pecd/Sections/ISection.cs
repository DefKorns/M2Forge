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
    /// Interface for a section.
    /// </summary>
    public interface ISection
    {
        /// <summary>
        /// Reads the section from a <see cref="BinaryReader"/>.
        /// </summary>
        /// <param name="br">The <see cref="BinaryReader"/> to read from.</param>
        /// <param name="length">The length of the section.</param>
        /// <param name="crypter">The <see cref="Crypter"/> to use for decryption.</param>
        void Read(BinaryReader br, int length, Crypter crypter);
        /// <summary>
        /// Writes the section to a <see cref="BinaryWriter"/>.
        /// </summary>
        /// <param name="bw">The <see cref="BinaryWriter"/> to write to.</param>
        /// <param name="crypter">The <see cref="Crypter"/> to use for encryption.</param>
        /// <returns>The length of the section.</returns>
        int Write(BinaryWriter bw, Crypter crypter);
    }
}
