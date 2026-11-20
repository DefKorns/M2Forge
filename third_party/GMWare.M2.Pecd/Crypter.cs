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
namespace GMWare.M2.Pecd
{
    /// <summary>
    /// Handles PECD data encryption.
    /// </summary>
    public class Crypter
    {
        /// <summary>
        /// Gets or sets the archive key.
        /// </summary>
        public uint ArchKey { get; set; }

        /// <summary>
        /// Encrypts or decrypts <paramref name="buffer"/>.
        /// </summary>
        /// <param name="buffer">The buffer to encrypt or decrypt.</param>
        /// <param name="fileKey">The key associated with the buffer.</param>
        public void Crypt(byte[] buffer, uint fileKey)
        {
            if (ArchKey == 0) return;

            const ulong a = 0xffffff4e;
            const uint m = 0xfffffffe;

            uint Q = ArchKey;
            uint c = fileKey % (uint)a;

            uint key = 0;
            for (int i = 0; i < buffer.Length; ++i)
            {
                if (i % 4 == 0)
                {
                    // CMWC PRNG, see https://en.wikipedia.org/wiki/Multiply-with-carry_pseudorandom_number_generator
                    ulong t = a * Q + c;
                    c = (uint)(t >> 32);
                    uint x = (uint)t + c;
                    if (x < c)
                    {
                        ++x;
                        ++c;
                    }
                    Q = m - x;

                    key = Q;
                }

                buffer[i] ^= (byte)key;
                key >>= 8;
            }
        }
    }
}
