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
    /// Represents sprite replacement information.
    /// </summary>
    public class SpriteReplacement
    {
        /// <summary>
        /// Gets or sets the original sprite.
        /// </summary>
        public ushort[] OriginalSprite { get; set; } = new ushort[64];
        /// <summary>
        /// Gets or sets whether the sprite address is ignored.
        /// </summary>
        public bool IsGlobalReplacement { get; set; }
        /// <summary>
        /// Gets or sets the sprite address.
        /// </summary>
        public short Address { get; set; }
        /// <summary>
        /// Gets or sets the replacement sprite.
        /// </summary>
        public ushort[] ReplacementSprite { get; set; } = new ushort[64];

        /// <summary>
        /// Reads the info from <paramref name="br"/>.
        /// </summary>
        /// <param name="br">The <see cref="BinaryReader"/> to read from.</param>
        public void Read(BinaryReader br)
        {
            for (int i = 0; i < OriginalSprite.Length; ++i)
            {
                OriginalSprite[i] = br.ReadUInt16();
            }
            ushort idWord = br.ReadUInt16();
            IsGlobalReplacement = (idWord & 0x8000) != 0;
            Address = (short)(idWord & 0x7fff);
            for (int i = 0; i < ReplacementSprite.Length; ++i)
            {
                ReplacementSprite[i] = br.ReadUInt16();
            }
        }

        /// <summary>
        /// Writes the info to <paramref name="bw"/>.
        /// </summary>
        /// <param name="bw">The <see cref="BinaryWriter"/> to write to.</param>
        public void Write(BinaryWriter bw)
        {
            foreach (var s in OriginalSprite)
            {
                bw.Write(s);
            }
            bw.Write((ushort)((ushort)Address | (IsGlobalReplacement ? 0x8000 : 0)));
            foreach (var s in ReplacementSprite)
            {
                bw.Write(s);
            }
        }

        /// <summary>
        /// Converts the original sprite to indexed pixels.
        /// </summary>
        /// <returns>The converted sprite.</returns>
        public byte[] ExtractOriginalSprite()
        {
            return ExtractPixels(OriginalSprite);
        }

        /// <summary>
        /// Converts the replacement sprite to indexed pixels.
        /// </summary>
        /// <returns>The converted sprite.</returns>
        public byte[] ExtractReplacementSprite()
        {
            return ExtractPixels(ReplacementSprite);
        }

        /// <summary>
        /// Imports indexed pixels into the original sprite.
        /// </summary>
        /// <param name="pixels">The pixels to import.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="pixels"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Thrown if <paramref name="pixels"/> is not 256 bytes in length.</exception>
        public void ImportOriginalSprite(byte[] pixels)
        {
            ImportPixels(pixels, OriginalSprite);
        }

        /// <summary>
        /// Imports indexed pixels into the replacement sprite.
        /// </summary>
        /// <param name="pixels">The pixels to import.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="pixels"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Thrown if <paramref name="pixels"/> is not 256 bytes in length.</exception>
        public void ImportReplacementSprite(byte[] pixels)
        {
            ImportPixels(pixels, ReplacementSprite);
        }

        byte[] ExtractPixels(ushort[] sprite)
        {
            byte[] pixels = new byte[256];

            for (int line = 0; line < 16; ++line)
            {
                ushort sg0 = sprite[line];
                for (int col = 0; col < 16; ++col)
                {
                    pixels[line * 16 + 15 - col] |= (byte)(sg0 & 1);
                    sg0 >>= 1;
                }
            }

            for (int line = 0; line < 16; ++line)
            {
                ushort sg1 = sprite[16 + line];
                for (int col = 0; col < 16; ++col)
                {
                    pixels[line * 16 + 15 - col] |= (byte)((sg1 & 1) << 1);
                    sg1 >>= 1;
                }
            }

            for (int line = 0; line < 16; ++line)
            {
                ushort sg2 = sprite[32 + line];
                for (int col = 0; col < 16; ++col)
                {
                    pixels[line * 16 + 15 - col] |= (byte)((sg2 & 1) << 2);
                    sg2 >>= 1;
                }
            }

            for (int line = 0; line < 16; ++line)
            {
                ushort sg3 = sprite[48 + line];
                for (int col = 0; col < 16; ++col)
                {
                    pixels[line * 16 + 15 - col] |= (byte)((sg3 & 1) << 3);
                    sg3 >>= 1;
                }
            }

            return pixels;
        }

        void ImportPixels(byte[] pixels, ushort[] sprite)
        {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            if (pixels.Length != 256) throw new ArgumentException("Incorrect sprite size.", nameof(pixels));

            // Clear tile
            for (int i = 0; i < sprite.Length; ++i)
            {
                sprite[i] = 0;
            }

            for (int i = 0; i < pixels.Length; ++i)
            {
                int col = i % 16;
                int line = i / 16;
                byte pixel = pixels[i];

                // sg0
                sprite[line] |= (ushort)((pixel & 1) << (15 - col));
                // sg1
                sprite[16 + line] |= (ushort)(((pixel & 2) >> 1) << (15 - col));
                // sg2
                sprite[32 + line] |= (ushort)(((pixel & 4) >> 2) << (15 - col));
                // sg3
                sprite[48 + line] |= (ushort)(((pixel & 8) >> 3) << (15 - col));
            }
        }
    }
}
