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
using Concentus;
using Concentus.Structs;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GMWare.M2.Pecd
{
    /// <summary>
    /// Processes audio samples and converts to Opus encoded chunks.
    /// </summary>
    public class AudioChunkGenerator : IDisposable
    {
        Stream destStream;
        BinaryWriter destBw;
        List<int> chunkOffsets = new List<int>();
        int sampleRate;
        int channels;
        OpusEncoder encoder;
        int frameSize;
        bool finalized;
        int readBlockSize;
        Pipe masterPipe;
        bool relaxPositionCheck;
        float[] silence;
        Task processorTask;

        /// <summary>
        /// Gets a list of chunk offsets.
        /// </summary>
        public IList<int> ChunkOffsets => chunkOffsets;
        /// <summary>
        /// Gets the number of converted samples written.
        /// </summary>
        public long SamplesWritten { get; private set; }
        /// <summary>
        /// Gets the encoder's number of lookahead samples.
        /// </summary>
        public int Lookahead => encoder.Lookahead;

        /// <summary>
        /// Instantiates a new instance of <see cref="AudioChunkGenerator"/>.
        /// </summary>
        /// <param name="sampleRate">The sample rate of the audio being written.</param>
        /// <param name="channels">The number of channels of the audio.</param>
        /// <param name="stream">The <see cref="Stream"/> to write chunks to.</param>
        /// <param name="frameSize">The Opus encoder frame size.</param>
        /// <param name="silence">One chunk's worth of silence samples if optimizing away silent sections.</param>
        /// <param name="relaxPositionCheck">If <c>true</c>, do not check for noncontinuous audio.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if number of channels is less than 1.</exception>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="stream"/> is <c>null</c>.</exception>
        public AudioChunkGenerator(int sampleRate, int channels, Stream stream, int frameSize, float[] silence, bool relaxPositionCheck = false)
        {
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels), "Less than 1 channel specified.");
            destStream = stream ?? throw new ArgumentNullException(nameof(stream));
            destBw = new BinaryWriter(stream);
            this.sampleRate = sampleRate;
            this.channels = channels;
            encoder = new OpusEncoder(sampleRate, channels, Concentus.Enums.OpusApplication.OPUS_APPLICATION_AUDIO)
            {
                UseConstrainedVBR = false,
                Bitrate = 192000
            };
            this.frameSize = frameSize;
            readBlockSize = sampleRate * channels * sizeof(float);
            masterPipe = new Pipe();
            this.relaxPositionCheck = relaxPositionCheck;
            this.silence = silence;
        }

        /// <summary>
        /// Asynchronously loads samples from a <paramref name="reader"/>.
        /// </summary>
        /// <param name="position">The samples position this range of samples start at.</param>
        /// <param name="reader">The <see cref="PipeReader"/> to read samples from.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task ProcessAsync(long position, PipeReader reader)
        {
            CheckFinished();
            CheckPosition(position);
            await FillGap(position);
            long bytesRead = 0;
            while (true)
            {
                var read = await reader.ReadAsync();
                foreach (var segment in read.Buffer)
                {
                    await masterPipe.Writer.WriteAsync(segment);
                    bytesRead += segment.Length;
                }
                reader.AdvanceTo(read.Buffer.End);
                if (read.IsCompleted) break;
            }
            SamplesWritten += bytesRead / sizeof(float) / channels;
        }

        void CheckPosition(long position)
        {
            if (position < SamplesWritten)
            {
                throw new ArgumentException("Position is before last written position.", nameof(position));
            }
            else if (position > SamplesWritten && !relaxPositionCheck)
            {
                // Ensure the gap is less than 1 frame
                if (position - SamplesWritten >= sampleRate / Utils.FRAMES_PER_SECOND)
                {
                    //Console.Error.WriteLine($"Sample pos check failed: {position - SamplesWritten} samples");
                    throw new ArgumentException($"Position is far too much ahead of last written position. ({position - SamplesWritten} samples)", nameof(position));
                }
            }
        }

        async Task FillGap(long position)
        {
            if (position >= SamplesWritten) return;

            long fillLength = position - SamplesWritten;
            if (fillLength > sampleRate)
            {
                byte[] oneSecondSilence = new byte[sampleRate * channels * sizeof(float)];
                while (fillLength > sampleRate)
                {
                    await masterPipe.Writer.WriteAsync(oneSecondSilence);
                    fillLength = position - SamplesWritten;
                }
            }

            if (fillLength > 0)
            {
                byte[] silence = new byte[fillLength * channels * sizeof(float)];
                await masterPipe.Writer.WriteAsync(silence);
            }

            SamplesWritten = position;
        }

        /// <summary>
        /// Starts background processing of samples.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public Task BeginProcessing()
        {
            if (processorTask != null) throw new InvalidOperationException("Processor already started.");
            processorTask = ProcessorAsync();
            return processorTask;
        }

        async Task ProcessorAsync()
        {
            CheckFinished();
            float[] incomingBuffer = new float[sampleRate * channels];
            Memory<float> incomingMemory = incomingBuffer;

            while (true)
            {
                ReadResult read = await masterPipe.Reader.ReadAsync();
                var buffer = read.Buffer;

                while (buffer.Length >= readBlockSize)
                {
                    var data = buffer.Slice(0, readBlockSize);
                    CopyToMemory(data, incomingMemory);
                    ConvertChunk(incomingBuffer);
                    buffer = buffer.Slice(readBlockSize);
                }
                masterPipe.Reader.AdvanceTo(buffer.Start, read.Buffer.End);

                if (read.IsCompleted) break;
            }

            ReadResult finalRead = await masterPipe.Reader.ReadAsync();
            var finalBuffer = finalRead.Buffer;
            if (finalBuffer.Length > 0)
            {
                float[] finalArrBuf = new float[finalBuffer.Length / sizeof(float)];
                Memory<float> finalMemory = finalArrBuf;
                CopyToMemory(finalBuffer, finalMemory);
                if (finalArrBuf.Length / channels < frameSize)
                    Array.Resize(ref finalArrBuf, frameSize * channels);
                ConvertChunk(finalArrBuf);

                masterPipe.Reader.AdvanceTo(finalBuffer.End);
            }

            masterPipe.Reader.Complete();
        }

        void ConvertChunk(float[] buffer)
        {
            if (silence != null && ArrayCompare<float>(silence, buffer))
            {
                chunkOffsets.Add((int)destStream.Position);
                return;
            }

            List<int> lengths = new List<int>();
            byte[] frameBuffer = new byte[1275]; // Max Opus packet size
            int bufferLeft = buffer.Length;
            int offset = 0;
            using (MemoryStream ms = new MemoryStream())
            {
                while (bufferLeft > 0)
                {
                    int packetSize;
                    int bufferFrames = Math.Min(frameSize, bufferLeft / channels);
                    if (bufferFrames < frameSize)
                    {
                        float[] localSamples = new float[frameSize * channels];
                        Buffer.BlockCopy(buffer, offset, localSamples, 0, bufferFrames * channels * sizeof(float));
                        packetSize = encoder.Encode(localSamples, 0, frameSize, frameBuffer, 0, frameBuffer.Length);
                    }
                    else
                    {
                        packetSize = encoder.Encode(buffer, offset, bufferFrames, frameBuffer, 0, frameBuffer.Length);
                    }
                    System.Diagnostics.Debug.Assert(packetSize > 0);
                    ms.Write(frameBuffer, 0, packetSize);
                    lengths.Add(packetSize);
                    bufferLeft -= bufferFrames * channels;
                    offset += bufferFrames * channels;
                }
                ms.Flush();

                chunkOffsets.Add((int)destStream.Position);
                LengthArrayEncoder.Encode(lengths, destBw);
                destBw.Write(ms.ToArray());
            }
        }

        /// <summary>
        /// Finalizes chunk writing.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task FinishAsync()
        {
            CheckFinished();
            masterPipe.Writer.Complete();
            await processorTask;
            destBw.Flush();
            chunkOffsets.Add((int)destStream.Position);
            finalized = true;
        }

        // https://stackoverflow.com/a/48599119
        static bool ArrayCompare<T>(ReadOnlySpan<T> a1, ReadOnlySpan<T> a2) where T : IEquatable<T>
        {
            return a1.SequenceEqual(a2);
        }

        static void CopyToMemory(ReadOnlySequence<byte> inSeq, Memory<float> outMemory)
        {
            Span<float> memSpan = outMemory.Span;
            Span<byte> memByteSpan = MemoryMarshal.Cast<float, byte>(memSpan);
            inSeq.CopyTo(memByteSpan);
        }

        void CheckFinished()
        {
            if (finalized) throw new InvalidOperationException("Generator has finalized.");
        }

        /// <summary>
        /// Disposes this instance.
        /// </summary>
        public void Dispose()
        {
            if (!finalized)
            {
                FinishAsync().GetAwaiter().GetResult();
            }
        }
    }
}
