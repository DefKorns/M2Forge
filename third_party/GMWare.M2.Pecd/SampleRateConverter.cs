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
using Concentus.Common;
using System;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GMWare.M2.Pecd
{
    /// <summary>
    /// Interpolates audio samples between sample rates.
    /// </summary>
    public class SampleRateConverter : IDisposable
    {
        SpeexResampler resampler;
        private bool disposedValue;
        int channels;
        Pipe inputPipe;
        Pipe outputPipe;
        double ratio;
        bool floatInput;
        bool floatOutput;

        /// <summary>
        /// Gets the number of samples output.
        /// </summary>
        public int SamplesWritten { get; private set; }
        /// <summary>
        /// Gets the <see cref="PipeWriter"/> for samples to be converted.
        /// </summary>
        public PipeWriter Input => inputPipe.Writer;
        /// <summary>
        /// Gets the <see cref="PipeReader"/> for samples that have beeen converted.
        /// </summary>
        public PipeReader Output => outputPipe.Reader;

        /// <summary>
        /// Instantiates a new instance of <see cref="SampleRateConverter"/>.
        /// </summary>
        /// <param name="inputRate">The input sample rate.</param>
        /// <param name="outputRate">The output sample rate.</param>
        /// <param name="channels">The number of channels in the audio.</param>
        /// <param name="floatInput">Whether the input is floating point or 16-bit signed integer samples.</param>
        /// <param name="floatOutput">Whether the output should be floating point or 16-bit signed integer samples.</param>
        public SampleRateConverter(int inputRate, int outputRate, int channels, bool floatInput, bool floatOutput)
        {
            resampler = new SpeexResampler(channels, inputRate, outputRate, 10);
            ratio = (double)outputRate / inputRate;
            this.channels = channels;
            inputPipe = new Pipe();
            outputPipe = new Pipe();
            this.floatInput = floatInput;
            this.floatOutput = floatOutput;
        }

        void CheckDisposed()
        {
            if (disposedValue) throw new ObjectDisposedException(GetType().FullName);
        }

        /// <summary>
        /// Starts background processing of samples.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if instance has been disposed.</exception>
        public async Task ProcessAsync()
        {
            CheckDisposed();

            short[] inputBuffer = new short[96000];
            short[] outputBuffer = new short[96000];

            long samplesRead = 0;
            int latencyRemaining = resampler.OutputLatency * channels;
            while (true)
            {
                var read = await inputPipe.Reader.ReadAsync();
                if (!read.Buffer.IsEmpty)
                {
                    int inBufOffset = 0;
                    foreach (var segment in read.Buffer)
                    {
                        bool outerBreak = false;
                        if (floatInput)
                        {
                            foreach (var sample in MemoryMarshal.Cast<byte, float>(segment.Span).ToArray())
                            {
                                inputBuffer[inBufOffset++] = (short)(sample * -short.MinValue);
                                if (inBufOffset == inputBuffer.Length)
                                {
                                    outerBreak = true;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            var segArr = MemoryMarshal.Cast<byte, short>(segment.Span).ToArray();
                            int copyCount = Math.Min(inputBuffer.Length - inBufOffset, segArr.Length);
                            Buffer.BlockCopy(segArr, 0, inputBuffer, inBufOffset * sizeof(short), copyCount * sizeof(short));
                            inBufOffset += copyCount;
                            if (inBufOffset == inputBuffer.Length) break;
                        }
                        if (outerBreak) break;
                    }

                    int inputLengthUsed = inBufOffset / channels;
                    int outputLengthGenerated = outputBuffer.Length / channels;
                    resampler.ProcessInterleaved(inputBuffer, 0, ref inputLengthUsed, outputBuffer, 0, ref outputLengthGenerated);
                    inputLengthUsed *= channels;
                    outputLengthGenerated *= channels;

                    inputPipe.Reader.AdvanceTo(read.Buffer.GetPosition(inputLengthUsed * (floatInput ? sizeof(float) : sizeof(short))));
                    samplesRead += inputLengthUsed;

                    if (latencyRemaining > 0)
                    {
                        if (outputLengthGenerated < latencyRemaining)
                        {
                            latencyRemaining -= outputLengthGenerated;
                            continue;
                        }
                        else
                        {
                            outputLengthGenerated -= latencyRemaining;
                        }
                    }

                    if (floatOutput)
                    {
                        float[] outSamples = new float[outputLengthGenerated];
                        for (int i = 0; i < outputLengthGenerated; ++i)
                        {
                            outSamples[i] = Math.Clamp((float)outputBuffer[i + latencyRemaining] / -short.MinValue, -1F, 1F);
                        }
                        await outputPipe.Writer.WriteAsync(Utils.CastToBytes(outSamples, outputLengthGenerated));
                    }
                    else
                    {
                        await outputPipe.Writer.WriteAsync(Utils.CastToBytes(outputBuffer.AsMemory(latencyRemaining, outputLengthGenerated)));
                    }
                    latencyRemaining = 0;
                    SamplesWritten += outputLengthGenerated;
                }

                if (read.IsCompleted && read.Buffer.IsEmpty) break;
            }

            {
                // Fill buffer with input latency and finalize
                int inputLengthUsed = resampler.InputLatency;
                inputBuffer.AsSpan(0, inputLengthUsed * channels).Fill(0);
                int outputLengthGenerated = outputBuffer.Length / channels;
                resampler.ProcessInterleaved(inputBuffer, 0, ref inputLengthUsed, outputBuffer, 0, ref outputLengthGenerated);
                inputLengthUsed *= channels;
                outputLengthGenerated *= channels;

                // Try to cap number of samples to expected amount
                long targetSamples = (long)Math.Floor(samplesRead * ratio);
                long remainingSamples = targetSamples - SamplesWritten;
                if (remainingSamples > 0)
                {
                    outputLengthGenerated = (int)Math.Min(remainingSamples, outputLengthGenerated);
                    if (floatOutput)
                    {
                        float[] outSamples = new float[outputLengthGenerated];
                        for (int i = 0; i < outputLengthGenerated; ++i)
                        {
                            outSamples[i] = Math.Clamp((float)outputBuffer[i] / -short.MinValue, -1F, 1F);
                        }
                        await outputPipe.Writer.WriteAsync(Utils.CastToBytes(outSamples, outputLengthGenerated));
                    }
                    else
                    {
                        await outputPipe.Writer.WriteAsync(Utils.CastToBytes(outputBuffer, outputLengthGenerated));
                    }
                    SamplesWritten += outputLengthGenerated;
                }

                inputPipe.Reader.Complete();
                outputPipe.Writer.Complete();
            }
        }

        /// <summary>
        /// Disposes this instance
        /// </summary>
        /// <param name="disposing">True if explicitly disposing, false if in finalizer</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // Nothing here anymore
                }

                disposedValue = true;
            }
        }

        /// <summary>
        /// Disposes this instance.
        /// </summary>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
