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

namespace GMWare.M2.Pecd.Sections
{
    /// <summary>
    /// Represents a tile/sprite replacement set section.
    /// </summary>
    class Cgp0Section : ISection
    {
        /// <summary>
        /// The tag for the section.
        /// </summary>
        public const string TAG = "cgp0";

        /// <summary>
        /// Gets or sets tile replacements.
        /// </summary>
        public List<TileReplacement> TileReplacements { get; set; }
        /// <summary>
        /// Gets or sets sprite replacements.
        /// </summary>
        public List<SpriteReplacement> SpriteReplacements { get; set; }

        /// <inheritdoc/>
        public void Read(BinaryReader br, int length, Crypter crypter)
        {
            byte[] data = br.ReadBytes(length);
            crypter.Crypt(data, (uint)length);
            using (MemoryStream ms = new MemoryStream(data))
            {
                BinaryReader msbr = new BinaryReader(ms);
                short tileReplacementsCount = msbr.ReadInt16();
                TileReplacements = new List<TileReplacement>();
                for (int i = 0; i < tileReplacementsCount; ++i)
                {
                    TileReplacement tr = new TileReplacement();
                    tr.Read(msbr);
                    TileReplacements.Add(tr);
                }
                short spriteReplacementsCount = msbr.ReadInt16();
                SpriteReplacements = new List<SpriteReplacement>();
                for (int i = 0; i < spriteReplacementsCount; ++i)
                {
                    SpriteReplacement sr = new SpriteReplacement();
                    sr.Read(msbr);
                    SpriteReplacements.Add(sr);
                }
                System.Diagnostics.Debug.Assert(ms.Position == ms.Length);
            }
        }

        static int CompareArrays<T>(T[] x, T[] y) where T : IComparable<T>
        {
            int lengthDiff = x.Length.CompareTo(y.Length);
            if (lengthDiff != 0) return lengthDiff;

            for (int i = 0; i < x.Length; ++i)
            {
                int z = x[i].CompareTo(y[i]);
                if (z != 0) return z;
            }

            return 0;
        }

        static int CompareSpriteReplacement(SpriteReplacement x, SpriteReplacement y)
        {
            int z = CompareArrays(x.OriginalSprite, y.OriginalSprite);
            if (z != 0) return z;
            ushort xIdWord = (ushort)((ushort)x.Address | (x.IsGlobalReplacement ? 0x8000 : 0));
            ushort yIdWord = (ushort)((ushort)y.Address | (y.IsGlobalReplacement ? 0x8000 : 0));
            return xIdWord.CompareTo(yIdWord);
        }

        static int CompareTileReplacement(TileReplacement x, TileReplacement y)
        {
            int z = CompareArrays(x.OriginalTile, y.OriginalTile);
            if (z != 0) return z;
            ushort xIdWord = (ushort)((ushort)x.Address | (x.IsGlobalReplacement ? 0x8000 : 0));
            ushort yIdWord = (ushort)((ushort)y.Address | (y.IsGlobalReplacement ? 0x8000 : 0));
            return xIdWord.CompareTo(yIdWord);
        }

        /// <inheritdoc/>
        public int Write(BinaryWriter bw, Crypter crypter)
        {
            TileReplacements.Sort(CompareTileReplacement);
            SpriteReplacements.Sort(CompareSpriteReplacement);
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryWriter msbw = new BinaryWriter(ms);
                msbw.Write((short)TileReplacements.Count);
                foreach (var r in TileReplacements)
                {
                    r.Write(msbw);
                }
                msbw.Write((short)SpriteReplacements.Count);
                foreach (var r in SpriteReplacements)
                {
                    r.Write(msbw);
                }
                msbw.Flush();
                var data = ms.ToArray();
                crypter.Crypt(data, (uint)data.Length);
                bw.Write(data);
                return data.Length;
            }
        }
    }
}
