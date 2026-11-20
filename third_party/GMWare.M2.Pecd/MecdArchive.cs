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
using GMWare.M2.Pecd.Sections;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ZstdSharp;

namespace GMWare.M2.Pecd
{
    /// <summary>
    /// Represents a Mega Drive Mini 2 CD image.
    /// </summary>
    public class MecdArchive : CdArchive
    {
        /// <summary>
        /// The value at the beginning of the file identifying a PECD image.
        /// </summary>
        public const string MAGIC = "\0\0\0\0MECD";

        /// <summary>
        /// The order sections are written in.
        /// </summary>
        static readonly string[] DEFAULT_SECTION_ORDER =
        {
            ArchSection.TAG, InfoSection.TAG, BiosSection.TAG, OffsSection.TAG,
            AudzSection.TAG,
        };

        Compressor compressor = new Compressor(9);
        Decompressor decompressor = new Decompressor();

        /// <inheritdoc/>
        protected override string Magic => MAGIC;
        /// <inheritdoc/>
        protected override string[] DefaultSectionOrder => DEFAULT_SECTION_ORDER;

        /// <inheritdoc/>
        public override string ArchString => "megacd";
        /// <inheritdoc/>
        public override int DefaultSectorsPerChunk => 32;
        /// <inheritdoc/>
        public override bool SupportsPregap => false;
        /// <inheritdoc/>
        public override int PregapAdjustLength => 2 * Utils.FRAMES_PER_SECOND;

        /// <inheritdoc/>
        protected override ISection LoadSection(BinaryReader br, Crypter readCrypter, string tag, int length)
        {
            ISection section;
            switch (tag)
            {
                case ArchSection.TAG:
                    section = new ArchSection();
                    break;
                case InfoSection.TAG:
                    section = new InfoSection();
                    break;
                case OffsSection.TAG:
                    section = new OffsSection();
                    break;
                case AudzSection.TAG:
                    section = new AudzSection();
                    break;
                case BiosSection.TAG:
                    section = new BiosSection() { SkipCrypt = true };
                    break;
                default:
                    section = new BinarySection();
                    break;
            }
            section.Read(br, length, readCrypter);
            return section;
        }

        /// <inheritdoc/>
        protected override int DecompressChunk(byte[] compressed, byte[] decompressed)
        {
            var unwrapped = decompressor.Unwrap(compressed);
            unwrapped.CopyTo(decompressed);
            return unwrapped.Length;
        }

        /// <inheritdoc/>
        protected override byte[] CompressChunk(byte[] data)
        {
            var wrapped = compressor.Wrap(data);
            return wrapped.ToArray();
        }
    }
}
