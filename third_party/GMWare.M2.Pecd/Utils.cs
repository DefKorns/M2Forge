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
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GMWare.M2.Pecd
{
    /// <summary>
    /// Contains utility functions.
    /// </summary>
    public static class Utils
    {
        /// <summary>
        /// The number of CD frames per second.
        /// </summary>
        public const int FRAMES_PER_SECOND = 75;
        /// <summary>
        /// The number of bytes per sector for data chunks.
        /// </summary>
        public const int PECD_BYTES_PER_SECTOR = 2048;

        /// <summary>
        /// Converts LSN to MSF.
        /// </summary>
        /// <param name="lsn">The logical sector number.</param>
        /// <returns>A time in minutes/seconds/frames format.</returns>
        public static string LsnToMsf(int lsn)
        {
            int frame = lsn % FRAMES_PER_SECOND;
            lsn /= FRAMES_PER_SECOND;
            int seconds = lsn % 60;
            int mins = lsn / 60;
            return $"{mins:d2}:{seconds:d2}:{frame:d2}";
        }

        /// <summary>
        /// Converts LSN to length in bytes.
        /// </summary>
        /// <param name="lsn">The logical sector number.</param>
        /// <param name="frameSize">The number of bytes per frame.</param>
        /// <returns>The length in bytes for the given LSN.</returns>
        public static long LsnToBytes(int lsn, int frameSize)
        {
            return lsn * frameSize;
        }

        /// <summary>
        /// Calculates the LSN of this index.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <returns>The LSN of the index.</returns>
        public static int GetLsn(this CueSharp.Index index)
        {
            return index.Frames + index.Seconds * FRAMES_PER_SECOND + index.Minutes * 60 * FRAMES_PER_SECOND;
        }

        /// <summary>
        /// Determines if this index is valid.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <returns>Whether this index is valid.</returns>
        public static bool IsIndexValid(this CueSharp.Index index)
        {
            return index.Frames > 0 || index.Seconds > 0 || index.Minutes > 0;
        }

        /// <summary>
        /// Gets an index by its number.
        /// </summary>
        /// <param name="track">The track.</param>
        /// <param name="index">The index number.</param>
        /// <returns>The index associated with that number.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no index with the number is found.</exception>
        public static CueSharp.Index GetIndex(this CueSharp.Track track, int index)
        {
            return track.Indices.Single(i => i.Number == index);
        }

        /// <summary>
        /// Converts an array to a <see cref="Memory{Byte}"/>.
        /// </summary>
        /// <typeparam name="T">The array's type.</typeparam>
        /// <param name="arr">The array to convert.</param>
        /// <param name="length">The length to take from the array.</param>
        /// <returns>The <see cref="Memory{Byte}"/> made from the array.</returns>
        public static Memory<byte> CastToBytes<T>(T[] arr, int length) where T : struct
        {
            Span<T> span = arr.AsSpan(0, length);
            var casted = MemoryMarshal.AsBytes(span);
            return casted.ToArray().AsMemory();
        }

        /// <summary>
        /// Converts a <see cref="Memory{T}"/> to a <see cref="Memory{Byte}"/>.
        /// </summary>
        /// <typeparam name="T">The type of the memory element.</typeparam>
        /// <param name="mem">The memory.</param>
        /// <returns>The <see cref="Memory{Byte}"/> made from the memory.</returns>
        public static Memory<byte> CastToBytes<T>(Memory<T> mem) where T : struct
        {
            Span<T> span = mem.Span;
            var casted = MemoryMarshal.AsBytes(span);
            return casted.ToArray().AsMemory();
        }

        /// <summary>
        /// Copies data from a <see cref="PipeReader"/> to a <see cref="Stream"/>.
        /// </summary>
        /// <param name="reader">The <see cref="PipeReader"/> to copy from.</param>
        /// <param name="stream">The <see cref="Stream"/> to write to.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="reader"/> or <paramref name="stream"/> is <c>null</c>.
        /// </exception>
        public static async Task CopyPipelineToStream(PipeReader reader, Stream stream)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            while (true)
            {
                var read = await reader.ReadAsync();
                if (!read.Buffer.IsEmpty)
                {
                    foreach (var segment in read.Buffer)
                    {
                        await stream.WriteAsync(segment);
                    }

                    reader.AdvanceTo(read.Buffer.End);
                }

                if (read.IsCompleted) break;
            }
            reader.Complete();
        }
    }
}
