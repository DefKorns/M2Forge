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
    /// <summary>
    /// Represents tile replacement information.
    /// </summary>
    public class TileReplacement
    {
        /// <summary>
        /// Gets or sets the original tile.
        /// </summary>
        public ushort[] OriginalTile { get; set; } = new ushort[16];
        /// <summary>
        /// Gets or sets whether the tile address is ignored.
        /// </summary>
        public bool IsGlobalReplacement { get; set; }
        /// <summary>
        /// Gets or sets the tile address.
        /// </summary>
        public short Address { get; set; }
        /// <summary>
        /// Gets or sets the replacement tile.
        /// </summary>
        public ushort[] ReplacementTile { get; set; } = new ushort[16];

        /// <summary>
        /// Reads the info from <paramref name="br"/>.
        /// </summary>
        /// <param name="br">The <see cref="BinaryReader"/> to read from.</param>
        public void Read(BinaryReader br)
        {
            for (int i = 0; i < OriginalTile.Length; ++i)
            {
                OriginalTile[i] = br.ReadUInt16();
            }
            ushort idWord = br.ReadUInt16();
            IsGlobalReplacement = (idWord & 0x8000) != 0;
            Address = (short)(idWord & 0x7fff);
            for (int i = 0; i < ReplacementTile.Length; ++i)
            {
                ReplacementTile[i] = br.ReadUInt16();
            }
        }

        /// <summary>
        /// Writes the info to <paramref name="bw"/>.
        /// </summary>
        /// <param name="bw">The <see cref="BinaryWriter"/> to write to.</param>
        public void Write(BinaryWriter bw)
        {
            foreach (var s in OriginalTile)
            {
                bw.Write(s);
            }
            bw.Write((ushort)((ushort)Address | (IsGlobalReplacement ? 0x8000 : 0)));
            foreach (var s in ReplacementTile)
            {
                bw.Write(s);
            }
        }

        /// <summary>
        /// Converts the original tile to indexed pixels.
        /// </summary>
        /// <returns>The converted tile.</returns>
        public byte[] ExtractOriginalTile()
        {
            return ExtractPixels(OriginalTile);
        }

        /// <summary>
        /// Converts the replacement tile to indexed pixels.
        /// </summary>
        /// <returns>The converted tile.</returns>
        public byte[] ExtractReplacementTile()
        {
            return ExtractPixels(ReplacementTile);
        }

        /// <summary>
        /// Imports indexed pixels into the original tile.
        /// </summary>
        /// <param name="pixels">The pixels to import.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="pixels"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Thrown if <paramref name="pixels"/> is not 64 bytes in length.</exception>
        public void ImportOriginalTile(byte[] pixels)
        {
            ImportPixels(pixels, OriginalTile);
        }

        /// <summary>
        /// Imports indexed pixels into the replacement tile.
        /// </summary>
        /// <param name="pixels">The pixels to import.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="pixels"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Thrown if <paramref name="pixels"/> is not 64 bytes in length.</exception>
        public void ImportReplacementTile(byte[] pixels)
        {
            ImportPixels(pixels, ReplacementTile);
        }

        byte[] ExtractPixels(ushort[] tile)
        {
            byte[] pixels = new byte[64];

            // Process byte 1 and 2 of each line
            for (int line = 0; line < 8; ++line)
            {
                byte ch0 = (byte)tile[line];
                byte ch1 = (byte)(tile[line] >> 8);
                // Shift out the bits
                for (int col = 0; col < 8; ++col)
                {
                    pixels[line * 8 + 7 - col] |= (byte)((ch0 & 1) | ((ch1 & 1) << 1));
                    ch0 >>= 1;
                    ch1 >>= 1;
                }
            }

            // Process byte 3 and 4 of each line
            for (int line = 0; line < 8; ++line)
            {
                byte ch2 = (byte)tile[8 + line];
                byte ch3 = (byte)(tile[8 + line] >> 8);
                // Shift out the bits
                for (int col = 0; col < 8; ++col)
                {
                    pixels[line * 8 + 7 - col] |= (byte)(((ch2 & 1) << 2) | ((ch3 & 1) << 3));
                    ch2 >>= 1;
                    ch3 >>= 1;
                }
            }

            return pixels;
        }

        void ImportPixels(byte[] pixels, ushort[] tile)
        {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            if (pixels.Length != 64) throw new ArgumentException("Incorrect tile size.", nameof(pixels));

            // Clear tile
            for (int i = 0; i < tile.Length; ++i)
            {
                tile[i] = 0;
            }

            for (int i = 0; i < pixels.Length; ++i)
            {
                int col = i % 8;
                int line = i / 8;
                byte pixel = pixels[i];

                // ch0/ch1
                tile[line] |= (ushort)(((pixel & 1) << (7 - col)) | (((pixel & 2) >> 1) << (7 - col) << 8));
                // ch2/ch3
                tile[8 + line] |= (ushort)((((pixel & 4) >> 2) << (7 - col)) | (((pixel & 8) >> 3) << (7 - col) << 8));
            }
        }
    }
}
