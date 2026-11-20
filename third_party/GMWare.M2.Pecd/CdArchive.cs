// SPDX-License-Identifier: GPL-3.0-or-later
/*
 * GMWare.M2.Pecd: Library for manipulating M2 CD images used in M2 mini consoles
 * Copyright (C) 2020, 2022, 2024  Yukai Li
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
using Concentus.Structs;
using GMWare.M2.Pecd.Sections;
using K4os.Compression.LZ4;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace GMWare.M2.Pecd
{
    /// <summary>
    /// Represents an M2 CD image file.
    /// </summary>
    public abstract class CdArchive : IDisposable
    {
        /// <summary>
        /// The Opus sample rate used in M2 CD images.
        /// </summary>
        public const int OPUS_SAMPLE_RATE = 48000;
        /// <summary>
        /// The number of audio channels used in M2 CD images.
        /// </summary>
        public const int OPUS_CHANNELS = 2;

        Stream readStream;
        Stream writeStream;
        BinaryReader br;
        BinaryWriter bw;
        Crypter readCrypter;
        Crypter writeCrypter;
        bool disposed;

        /// <summary>
        /// Gets the magic number for identifying this type of image.
        /// </summary>
        protected abstract string Magic { get; }
        /// <summary>
        /// Gets the default order metadata sections are in.
        /// </summary>
        protected abstract string[] DefaultSectionOrder { get; }
        /// <summary>
        /// Gets the default architecture identifier.
        /// </summary>
        public abstract string ArchString { get; }
        /// <summary>
        /// Gets the default number of sectors for each compressed data chunk.
        /// </summary>
        public abstract int DefaultSectorsPerChunk { get; }
        /// <summary>
        /// Gets whether this archive format supports pregap.
        /// </summary>
        public abstract bool SupportsPregap { get; }
        /// <summary>
        /// Gets pregap length when archive does not support pregap.
        /// </summary>
        public abstract int PregapAdjustLength { get; }

        /// <summary>
        /// Gets or sets the metadata sections.
        /// </summary>
        public Dictionary<string, ISection> Sections { get; set; } = new Dictionary<string, ISection>();
        /// <summary>
        /// Gets or sets the list of disc info sections.
        /// </summary>
        public List<InfoSection> DiscInfos { get; set; } = new List<InfoSection>();

        /// <summary>
        /// Decompresses the chunk using the format's specific decompression codec.
        /// </summary>
        /// <param name="compressed">The buffer containing compressed data.</param>
        /// <param name="decompressed">The buffer to decompress data into.</param>
        /// <returns>The number of bytes written to <paramref name="decompressed"/>.</returns>
        protected abstract int DecompressChunk(byte[] compressed, byte[] decompressed);
        /// <summary>
        /// Compresses the chunk using the format's specific compression codec.
        /// </summary>
        /// <param name="data">The buffer containing data to compress.</param>
        /// <returns>The compressed data.</returns>
        protected abstract byte[] CompressChunk(byte[] data);

        /// <summary>
        /// Opens the image for reading.
        /// </summary>
        /// <param name="path">The path of the file to read from.</param>
        /// <exception cref="InvalidOperationException">Thrown if file is already opened for reading.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if instance has been disposed.</exception>
        public void OpenRead(string path)
        {
            if (readStream != null) throw new InvalidOperationException("File already opened for reading.");
            CheckDisposed();
            OpenRead(File.OpenRead(path));
        }

        /// <summary>
        /// Opens the image for reading.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <exception cref="InvalidOperationException">Thrown if file is already opened for reading.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if instance has been disposed.</exception>
        /// <exception cref="ArgumentException">Thrown if <paramref name="stream"/> is not seekable.</exception>
        public void OpenRead(Stream stream)
        {
            if (readStream != null) throw new InvalidOperationException("File already openened for reading.");
            if (!stream.CanSeek) throw new ArgumentException("Stream is not seekable.", nameof(stream));
            CheckDisposed();
            readStream = stream;
            br = new BinaryReader(readStream);
            if (new string(br.ReadChars(Magic.Length)) != Magic)
                throw new InvalidDataException("File is not a M2 CD image.");
            readCrypter = new Crypter();
            Sections = new Dictionary<string, ISection>();
            while (true)
            {
                string tag = new string(br.ReadChars(4));
                if (tag == "\0\0\0\0") break;
                int length = br.ReadInt32();
                ISection section = LoadSection(br, readCrypter, tag, length);
                if (section is InfoSection ifo)
                    DiscInfos.Add(ifo);
                else
                    Sections.Add(tag, section);
            }
        }

        /// <summary>
        /// Opens the image for creating new image.
        /// </summary>
        /// <param name="path">The path to write image to.</param>
        /// <param name="key">The archive key for encryption.</param>
        /// <exception cref="InvalidOperationException">Thrown if the image is already opened for writing.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if instance has been disposed.</exception>
        public void OpenWrite(string path, uint key = 0)
        {
            if (writeStream != null) throw new InvalidOperationException("File already opened for writing.");
            CheckDisposed();
            OpenWrite(File.Create(path), key);
        }

        /// <summary>
        /// Opens the image for creating new image.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="key">The archive key for encryption.</param>
        /// <exception cref="InvalidOperationException">Thrown if the image is already opened for writing.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if instance has been disposed.</exception>
        /// <exception cref="ArgumentException">Thrown if <paramref name="stream"/> is not seekable.</exception>
        public void OpenWrite(Stream stream, uint key = 0)
        {
            if (writeStream != null) throw new InvalidOperationException("File already opened for writing.");
            if (!stream.CanSeek) throw new ArgumentException("Stream is not seekable.", nameof(stream));
            CheckDisposed();
            writeStream = stream;
            bw = new BinaryWriter(writeStream);
            writeCrypter = new Crypter { ArchKey = key };
        }

        /// <summary>
        /// Gets a chunk from the image.
        /// </summary>
        /// <param name="isAudio">Whether to retrieve an audio chunk.</param>
        /// <param name="index">The index of the chunk to retrieve.</param>
        /// <returns>The chunk data.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if instance has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if image has not been opened for reading.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the index is invalid.</exception>
        /// <remarks>Data chunks are not decompressed when retrieved with this function.</remarks>
        /// <seealso cref="GetDataChunk(int)"/>
        public byte[] GetChunk(bool isAudio, int index)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index), index, "Index cannot be negative.");
            CheckRead();
            OffsSection offs = Sections[OffsSection.TAG] as OffsSection;
            if (!isAudio && index >= offs.DataChunksCount)
                throw new ArgumentOutOfRangeException(nameof(index), index, "Index exceeds available chunks.");
            int offsetIndex = index + (isAudio ? offs.DataChunksCount : 0);
            if (offsetIndex >= offs.DataChunksCount + offs.AudioChunksCount)
                throw new ArgumentOutOfRangeException(nameof(index), index, "Index exceeds available chunks.");

            int offset = offs.Offsets[offsetIndex];
            int length = offs.Offsets[offsetIndex + 1] - offset;
            readStream.Seek(offset, SeekOrigin.Begin);
            byte[] data = br.ReadBytes(length);
            uint fileKey = (uint)index;
            if (isAudio)
                fileKey |= 0x20000;
            else
                fileKey |= 0x10000;
            readCrypter.Crypt(data, fileKey);
            return data;
        }

        /// <summary>
        /// Gets a decompressed data chunk from image.
        /// </summary>
        /// <param name="index">The index of the chunk.</param>
        /// <returns>The decompressed chunk.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if instance has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if image has not been opened for reading.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the index is invalid.</exception>
        /// <seealso cref="GetChunk(bool, int)"/>
        public byte[] GetDataChunk(int index)
        {
            byte[] compressed = GetChunk(false, index);
            OffsSection offs = Sections[OffsSection.TAG] as OffsSection;
            int numDecompressedSectors = offs.SectorsPerChunk;
            if ((index + 1) * offs.SectorsPerChunk > offs.TotalDataSectors)
                numDecompressedSectors = offs.TotalDataSectors % offs.SectorsPerChunk;
            byte[] decompressed = new byte[numDecompressedSectors * Utils.PECD_BYTES_PER_SECTOR];
            int decompLen = DecompressChunk(compressed, decompressed);
            if (decompLen != decompressed.Length) throw new Exception("Failed to decompress whole chunk.");
            return decompressed;
        }

        /// <summary>
        /// Writes sections to disk.
        /// </summary>
        /// <param name="path">The path of the directory to write sections into.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="path"/> is <c>null</c> or whitespaces.</exception>
        public void DumpSections(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            Crypter nullCrypter = new Crypter();
            foreach (var pair in Sections)
            {
                using (FileStream fs = File.Create(Path.Combine(path, pair.Key)))
                {
                    BinaryWriter bw = new BinaryWriter(fs);
                    pair.Value.Write(bw, nullCrypter);
                }
            }
            for (int i = 0; i < DiscInfos.Count; ++i)
            {
                var ifo = DiscInfos[i];
                string name = DiscInfos.Count == 1 ? InfoSection.TAG : $"{InfoSection.TAG}{i}";
                using (FileStream fs = File.Create(Path.Combine(path, name)))
                {
                    BinaryWriter bw = new BinaryWriter(fs);
                    ifo.Write(bw, nullCrypter);
                }
            }
        }

        /// <summary>
        /// Dumps disc table of contents info to <paramref name="sw"/>.
        /// </summary>
        /// <param name="sw">The <see cref="TextWriter"/> to print info to.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="sw"/> is <c>null</c>.</exception>
        public void DumpTocDetails(TextWriter sw)
        {
            if (sw == null) throw new ArgumentNullException(nameof(sw));

            int setTotalLsn = 0;
            for (int i = 0; i < DiscInfos.Count; ++i)
            {
                var disc = DiscInfos[i];
                sw.WriteLine($"{InfoSection.TAG} section: Disc {i + 1}");
                sw.WriteLine();
                sw.WriteLine("#  | Type  | 00 Start MSF    LSN | 00 Length MSF Sectors | 01 Start MSF    LSN | 01 Length MSF Sectors");
                sw.WriteLine("======================================================================================================");
                for (int j = 0; j < disc.Tracks.Count; ++j)
                {
                    var track = disc.Tracks[j];
                    sw.Write($"{j + 1:d2} | {(track.IsAudio ? "Audio" : "Data"),-5} | {Utils.LsnToMsf(track.Offset - track.Index00Length),-12} {track.Offset - track.Index00Length,6} | {Utils.LsnToMsf(track.Index00Length),-13} {track.Index00Length,7} | ");
                    sw.WriteLine($"{Utils.LsnToMsf(track.Offset),-12} {track.Offset,6} | {Utils.LsnToMsf(track.Index01Length),-13} {track.Index01Length,7}");
                }
                sw.WriteLine();
                setTotalLsn += disc.DiscLength;
                sw.WriteLine($"Disc length: {Utils.LsnToMsf(disc.DiscLength)} ({disc.DiscLength})");
                sw.WriteLine();
            }

            if (DiscInfos.Count > 1)
            {
                sw.WriteLine($"Set total length: {Utils.LsnToMsf(setTotalLsn)} ({setTotalLsn})");
                sw.WriteLine();
            }
        }

        /// <summary>
        /// Writes the offsets section in a textual representation.
        /// </summary>
        /// <param name="sw">The <see cref="TextWriter"/> to write to.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="sw"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">Thrown if there is no offsets section inside the image.</exception>
        public void DumpOffsets(TextWriter sw)
        {
            if (sw == null) throw new ArgumentNullException(nameof(sw));
            OffsSection offs = Sections[OffsSection.TAG] as OffsSection;
            if (offs == null) throw new InvalidOperationException("No offsets section present in image.");
            sw.WriteLine($"{OffsSection.TAG} section");
            sw.WriteLine($"{nameof(offs.TotalDataSectors)}: {offs.TotalDataSectors}");
            sw.WriteLine($"{nameof(offs.SectorsPerChunk)}: {offs.SectorsPerChunk}");
            sw.WriteLine($"{nameof(offs.DataChunksCount)}: {offs.DataChunksCount}");
            sw.WriteLine($"{nameof(offs.AudioChunksCount)}: {offs.AudioChunksCount}");

            for (int i = 0; i < offs.Offsets.Count - 1; ++i)
            {
                bool isAudio = i >= offs.DataChunksCount;
                sw.Write($"{i} {offs.Offsets[i]:x8} {offs.Offsets[i + 1] - offs.Offsets[i]:x8} {(isAudio ? "audio" : "data")}");
                if (isAudio)
                {
                    int chunkIndex = i - offs.DataChunksCount;
                    byte[] chunk = GetChunk(isAudio, chunkIndex);
                    sw.Write($" ({chunkIndex}) {Utils.LsnToMsf(chunkIndex * Utils.FRAMES_PER_SECOND)}/{chunkIndex * Utils.FRAMES_PER_SECOND}");
                    if (chunk.Length == 0)
                    {
                        sw.Write(" silence");
                    }
                    else
                    {
                        DumpAudioChunkInfo(sw, chunk);
                    }
                }
                else
                {
                    sw.Write($" {Utils.LsnToMsf(i * offs.SectorsPerChunk)}/{i * offs.SectorsPerChunk}");
                }
                sw.WriteLine();
            }

            sw.WriteLine($"{offs.Offsets[offs.Offsets.Count - 1]:x8}");
        }

        void DumpAudioChunkInfo(TextWriter sw, byte[] chunk)
        {
            using (BinaryReader br = new BinaryReader(new MemoryStream(chunk)))
            {
                var lengths = LengthArrayEncoder.Decode(br);
                int numSamples = 0;
                foreach (var length in lengths)
                {
                    byte[] packet = br.ReadBytes(length);
                    numSamples += OpusPacketInfo.GetNumSamples(packet, 0, packet.Length, OPUS_SAMPLE_RATE);
                }
                sw.Write($" S:{numSamples} {lengths.Count}");
                foreach (var length in lengths)
                {
                    sw.Write($" {length}");
                }
            }
        }

        /// <summary>
        /// Dumps audz section info to <paramref name="sw"/>.
        /// </summary>
        /// <param name="sw">The <see cref="TextWriter"/> to write to.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="sw"/> is <c>null</c>.</exception>
        public void DumpAudzInfo(TextWriter sw)
        {
            if (sw == null) throw new ArgumentNullException(nameof(sw));
            var audz = Sections[AudzSection.TAG] as AudzSection;
            sw.WriteLine($"{AudzSection.TAG} section");
            if (audz == null)
            {
                sw.WriteLine("Not present");
                return;
            }
            sw.WriteLine($"{nameof(audz.TotalSamples)}: {audz.TotalSamples}");
            sw.WriteLine($"{nameof(audz.PaddingSamples)}: {audz.PaddingSamples}");
            sw.Write($"{nameof(audz.Silence)}:");
            DumpAudioChunkInfo(sw, audz.Silence);
            sw.WriteLine();
        }

        /// <summary>
        /// Writes headers to the image file.
        /// </summary>
        /// <param name="dummyEncrypt">If <c>true</c>, disables encrypting the header sections.</param>
        /// <returns>The size of the headers in bytes.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if instance has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if image has not been opened for writing.</exception>
        public int WriteHeaders(bool dummyEncrypt = false)
        {
            CheckWrite();
            bw.Seek(0, SeekOrigin.Begin);
            HashSet<string> remainingSections = new HashSet<string>(Sections.Keys);
            bw.Write(Magic.ToCharArray());
            foreach (var tag in DefaultSectionOrder)
            {
                if (tag == InfoSection.TAG)
                {
                    bool ifoDummyEncrypt = dummyEncrypt;
                    foreach (var ifo in DiscInfos)
                    {
                        WriteSection(bw, tag, ifo, writeCrypter, ifoDummyEncrypt);
                        ifoDummyEncrypt = false;
                    }
                }
                else if (Sections.TryGetValue(tag, out ISection section))
                {
                    WriteSection(bw, tag, section, writeCrypter, dummyEncrypt);
                    remainingSections.Remove(tag);
                }
            }
            // Write unknown sections left over
            foreach (var tag in remainingSections)
            {
                WriteSection(bw, tag, Sections[tag], writeCrypter, dummyEncrypt);
            }
            bw.Write(0); // Header terminator
            return (int)writeStream.Position;
        }

        /// <summary>
        /// Writes a chunk to the file.
        /// </summary>
        /// <param name="chunk">The data to write.</param>
        /// <param name="isAudio">Whether this is an audio chunk.</param>
        /// <param name="index">The index of the chunk.</param>
        /// <exception cref="ObjectDisposedException">Thrown if instance has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if image has not been opened for writing.</exception>
        /// <remarks>
        /// Chunks are written in the order this function is called. <paramref name="index"/>
        /// is only used for encryption.
        /// </remarks>
        /// <seealso cref="WriteDataChunk(byte[], int)"/>
        public void WriteChunk(byte[] chunk, bool isAudio, int index)
        {
            CheckWrite();
            uint fileKey = (uint)(index);
            if (isAudio)
                fileKey |= 0x20000;
            else
                fileKey |= 0x10000;
            writeCrypter.Crypt(chunk, fileKey);
            bw.Write(chunk);
        }

        /// <summary>
        /// Compresses and writes data chunk to file.
        /// </summary>
        /// <param name="data">The data to write.</param>
        /// <param name="index">The index of the chunk.</param>
        /// <returns>The compressed size of the chunk.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if instance has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if image has not been opened for writing.</exception>
        /// <remarks>
        /// Chunks are written in the order this function is called. <paramref name="index"/>
        /// is only used for encryption.
        /// </remarks>
        /// <seealso cref="WriteChunk(byte[], bool, int)"/>
        public int WriteDataChunk(byte[] data, int index)
        {
            CheckWrite();
            byte[] compressedData = CompressChunk(data);
            WriteChunk(compressedData, false, index);
            return compressedData.Length;
        }

        /// <summary>
        /// Copies the image to a different file with the headers and sections decrypted.
        /// </summary>
        /// <param name="path">The path to save decrypted image to.</param>
        /// <exception cref="ObjectDisposedException">Thrown if instance has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if image has not been opened for reading.</exception>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="path"/> is <c>null</c> or whitespaces.</exception>
        public void DecryptTo(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            CheckRead();
            using (FileStream fs = File.Create(path))
            {
                Crypter dummyCrypter = new Crypter();
                HashSet<string> remainingSections = new HashSet<string>(Sections.Keys);
                BinaryWriter bw = new BinaryWriter(fs);
                bw.Write(Magic.ToCharArray());
                foreach (var tag in DefaultSectionOrder)
                {
                    if (tag == InfoSection.TAG)
                    {
                        bool dummyEncrypt = true;
                        foreach (var ifo in DiscInfos)
                        {
                            WriteSection(bw, tag, ifo, readCrypter, dummyEncrypt);
                            dummyEncrypt = false;
                        }
                    }
                    else if (Sections.TryGetValue(tag, out ISection section))
                    {
                        WriteSection(bw, tag, section, readCrypter, true);
                        remainingSections.Remove(tag);
                    }
                }
                // Write unknown sections left over
                foreach (var tag in remainingSections)
                {
                    WriteSection(bw, tag, Sections[tag], readCrypter, true);
                }
                bw.Write(0); // Header terminator

                // Write chunks
                OffsSection offs = Sections[OffsSection.TAG] as OffsSection;
                for (int i = 0; i < offs.DataChunksCount; ++i)
                {
                    byte[] chunk = GetChunk(false, i);
                    bw.Write(chunk);
                }
                for (int i = 0; i < offs.AudioChunksCount; ++i)
                {
                    byte[] chunk = GetChunk(true, i);
                    bw.Write(chunk);
                }
            }
        }

        /// <summary>
        /// Disposes this instance.
        /// </summary>
        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                if (readStream != null)
                {
                    readStream.Close();
                    readStream = null;
                    br = null;
                }
                if (writeStream != null)
                {
                    writeStream.Close();
                    writeStream = null;
                    bw = null;
                }
            }
        }

        void CheckDisposed()
        {
            if (disposed) throw new ObjectDisposedException(GetType().FullName);
        }

        void CheckRead()
        {
            CheckDisposed();
            if (readStream == null) throw new InvalidOperationException("Archive not opened for reading.");
        }

        void CheckWrite()
        {
            CheckDisposed();
            if (writeStream == null) throw new InvalidOperationException("Archive not opened for writing.");
        }
        
        /// <summary>
        /// Reads a section from <paramref name="br"/>.
        /// </summary>
        /// <param name="br">The <see cref="BinaryReader"/> to read from.</param>
        /// <param name="readCrypter">The <see cref="Crypter"/> to use for decryption.</param>
        /// <param name="tag">The tag of the section.</param>
        /// <param name="length">The length of the section.</param>
        /// <returns>The deserialized section.</returns>
        abstract protected ISection LoadSection(BinaryReader br, Crypter readCrypter, string tag, int length);

        void WriteSection(BinaryWriter bw, string tag, ISection section, Crypter crypter, bool dummyEncrypt)
        {
            bw.Write(tag.ToCharArray());
            long savedPos = bw.BaseStream.Position;
            bw.Write(0);
            int length = section.Write(bw, crypter);
            if (tag == InfoSection.TAG && dummyEncrypt)
            {
                // Write dummy key so we don't have to update offsets
                bw.Write(0);
                length += 4;
            }
            bw.BaseStream.Seek(savedPos, SeekOrigin.Begin);
            bw.Write(length);
            bw.BaseStream.Seek(length, SeekOrigin.Current);
        }

        /// <summary>
        /// Opens an M2 CD image for reading.
        /// </summary>
        /// <param name="path">The path to read from.</param>
        /// <returns>A <see cref="CdArchive"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="path"/> is <c>null</c> or whitespaces.</exception>
        public static CdArchive OpenReadDetect(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            Stream fs = File.OpenRead(path);
            try
            {
                return OpenReadDetect(fs);
            }
            catch
            {
                fs.Close();
                throw;
            }
        }

        /// <summary>
        /// Opens an M2 CD image for reading.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> to read from.</param>
        /// <returns>A <see cref="CdArchive"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="stream"/> is <c>null</c>.</exception>
        public static CdArchive OpenReadDetect(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            BinaryReader br = new BinaryReader(stream);
            
            CdArchive TryMatchArchive(string key, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type)
            {
                string magic = new string(br.ReadChars(key.Length));
                stream.Seek(0, SeekOrigin.Begin);
                if (magic == key)
                {
                    CdArchive arch = Activator.CreateInstance(type) as CdArchive;
                    arch.OpenRead(stream);
                    return arch;
                }

                return null;
            }

            CdArchive arch =
                TryMatchArchive(PecdArchive.MAGIC, typeof(PecdArchive)) ??
                TryMatchArchive(MecdArchive.MAGIC, typeof(MecdArchive));

            if (arch != null)
                return arch;
            else
                throw new ArgumentException("Cannot find matching archive type.", nameof(stream));
        }

        /// <summary>
        /// Creates an M2 CD image instance by alias.
        /// </summary>
        /// <param name="alias">The alias of the image type.</param>
        /// <returns>A <see cref="CdArchive"/> instance.</returns>
        /// <exception cref="ArgumentException">Thrown if <paramref name="alias"/> is not known.</exception>
        public static CdArchive InstantiateByAlias(string alias)
        {
            switch (alias)
            {
                case "pecd":
                    return new PecdArchive();
                case "mecd":
                    return new MecdArchive();
                default:
                    throw new ArgumentException("Alias not found.", nameof(alias));
            }
        }
    }
}
