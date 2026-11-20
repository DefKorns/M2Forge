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
    /// Represents a CD track.
    /// </summary>
    /// <remarks>Lengths and offset are in LSN (logical sector number) units.</remarks>
    public class Track
    {
        /// <summary>
        /// Gets or sets the length of index 00 (pregap).
        /// </summary>
        public int Index00Length { get; set; }
        /// <summary>
        /// Gets or sets whether this track is an audio track.
        /// </summary>
        public bool IsAudio { get; set; }
        /// <summary>
        /// Gets or sets the offset of index 01.
        /// </summary>
        public int Offset { get; set; }
        /// <summary>
        /// Gets or sets the length of index 01.
        /// </summary>
        public int Index01Length { get; set; }
    }
}
