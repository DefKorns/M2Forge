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
using System.Collections.Generic;
using System.IO;

namespace GMWare.M2.Pecd
{
    /// <summary>
    /// Contains methods for serializing and deserializing length arrays.
    /// </summary>
    public static class LengthArrayEncoder
    {
        /// <summary>
        /// Deserialize length array from <paramref name="br"/>.
        /// </summary>
        /// <param name="br">The <see cref="BinaryReader"/> to read from.</param>
        /// <returns>A list of lengths.</returns>
        public static IList<int> Decode(BinaryReader br)
        {
            byte count = br.ReadByte();
            List<int> lengths = new List<int>();
            for (int i = 0; i < count; ++i)
            {
                int length = br.ReadByte();
                if ((length & 0x80) != 0)
                {
                    length &= 0x7f;
                    length |= br.ReadByte() << 7;
                }
                lengths.Add(length);
            }
            return lengths;
        }

        /// <summary>
        /// Serializes length array to <paramref name="bw"/>.
        /// </summary>
        /// <param name="lengths">The array to serialize.</param>
        /// <param name="bw">The <see cref="BinaryWriter"/> to write to.</param>
        public static void Encode(IList<int> lengths, BinaryWriter bw)
        {
            if (lengths.Count > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(lengths), "List length cannot be encoded in a byte.");
            bw.Write((byte)lengths.Count);
            foreach (int length in lengths)
            {
                if (length > 0x7fff)
                    throw new ArgumentOutOfRangeException(nameof(lengths), "List contains entry that cannot be encoded.");
                byte firstByte = (byte)(length & 0x7f);
                if (length > 0x7f)
                    firstByte |= 0x80;
                bw.Write(firstByte);
                if (length > 0x7f)
                    bw.Write((byte)(length >> 7));
            }
        }
    }
}
