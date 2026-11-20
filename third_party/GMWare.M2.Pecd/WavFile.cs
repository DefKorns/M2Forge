//////////////////////////////////////////////////////////////////////////////////////
//**********************************************************************************//
//*  WavFile.cs :   A part of DWA (Digital Waveform Archive) project               *//
//**********************************************************************************//
//*                                                                                *//
//*  Info: WAV File parser.                                                        *//
//*                                                                                *//
//**********************************************************************************//
//*  Copyright (c)2002-2019, Alex Gavrilov. All rights reserved.                   *//
//*  Mail: Alex.Gavrilov.mail@gmail.com                                            *//
//*                                                                                *//
//*  USE, DISTRIBUTION AND REPRODUCTION OF THIS SOURCE IS GOVERNED BY A BSD-STYLE  *//
//*  SOURCE CODE LICENSE INCLUDED WITH THIS SOURCE. PLEASE READ THESE TERMS.       *//
//**********************************************************************************//
//////////////////////////////////////////////////////////////////////////////////////


using System;
using System.IO;
using System.Text;
using System.Diagnostics;

namespace Core
{
    class WavFile
    {
        public int samplesPerSecond { get; set; }
        public int samplesTotalCount { get; set; }
        public byte[] metaData { get; set; }
        public long DataStartPos { get; private set; }


        public WavFile()
        {
            samplesTotalCount = samplesPerSecond = -1;
            metaData = null;
        }


        /// <summary>
        /// Reading all samples of a 16-bit stereo wav file into arrays.
        /// </summary>

        public bool ReadHeader(BinaryReader reader)
        {
            try
            {
                // header (8 + 4 bytes):

                byte[] riffId = reader.ReadBytes(4);    // "RIFF"
                int fileSize = reader.ReadInt32();      // size of entire file
                byte[] typeId = reader.ReadBytes(4);    // "WAVE"

                if (Encoding.ASCII.GetString(typeId) != "WAVE") return false;

                // chunk 1 (8 + 16 or 18 bytes):

                byte[] fmtId = reader.ReadBytes(4);     // "fmt "
                int fmtSize = reader.ReadInt32();       // size of chunk in bytes
                int fmtCode = reader.ReadInt16();       // 1 - for PCM
                int channels = reader.ReadInt16();      // 1 - mono, 2 - stereo
                int sampleRate = reader.ReadInt32();    // sample rate per second
                int byteRate = reader.ReadInt32();      // bytes per second
                int dataAlign = reader.ReadInt16();     // data align
                int bitDepth = reader.ReadInt16();      // 8, 16, 24, 32, 64 bits

                if (fmtCode != 1) return false;     // not PCM
                if (channels != 2) return false;    // only Stereo files in this version
                if (bitDepth != 16) return false;   // only 16-bit in this version

                if (fmtSize == 18) // fmt chunk can be 16 or 18 bytes
                {
                    int fmtExtraSize = reader.ReadInt16();  // read extra bytes size
                    reader.ReadBytes(fmtExtraSize);         // skip over "INFO" chunk
                }

                // chunk 2 (8 bytes):

                byte[] dataId = reader.ReadBytes(4);    // "data"
                int dataSize = reader.ReadInt32();      // size of audio data

                Debug.Assert(Encoding.ASCII.GetString(dataId) == "data", "Data chunk not found!");

                samplesPerSecond = sampleRate;                  // sample rate (usually 44100)
                samplesTotalCount = dataSize / (bitDepth / 8);  // total samples count in audio data
                DataStartPos = reader.BaseStream.Position;
                reader.BaseStream.Seek(dataSize, SeekOrigin.Current);

                // metadata:

                long moreBytes = reader.BaseStream.Length - reader.BaseStream.Position;

                if (moreBytes > 0)
                {
                    metaData = reader.ReadBytes((int)moreBytes);
                }
            }
            catch
            {
                Debug.Fail("Failed to read file.");
                return false;
            }

            return true;
        }


        /// <summary>
        /// Writing all 16-bit stereo samples from arrays into wav file.
        /// </summary>

        public bool WriteHeader(BinaryWriter writer)
        {
            Debug.Assert((samplesTotalCount != -1) && (samplesPerSecond != -1),
                "No sample count or sample rate info!");

            try
            {
                int fileSize = 44 + samplesTotalCount * 2;

                if (metaData != null)
                {
                    fileSize += metaData.Length;
                }

                // header:

                writer.Write(Encoding.ASCII.GetBytes("RIFF"));  // "RIFF"
                writer.Write((Int32)fileSize);                  // size of entire file with 16-bit data
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));  // "WAVE"

                // chunk 1:

                writer.Write(Encoding.ASCII.GetBytes("fmt "));  // "fmt "
                writer.Write((Int32)16);                        // size of chunk in bytes
                writer.Write((Int16)1);                         // 1 - for PCM
                writer.Write((Int16)2);                         // only Stereo files in this version
                writer.Write((Int32)samplesPerSecond);          // sample rate per second (usually 44100)
                writer.Write((Int32)(4 * samplesPerSecond));    // bytes per second (usually 176400)
                writer.Write((Int16)4);                         // data align 4 bytes (2 bytes sample stereo)
                writer.Write((Int16)16);                        // only 16-bit in this version

                // chunk 2:

                writer.Write(Encoding.ASCII.GetBytes("data"));  // "data"
                writer.Write((Int32)(samplesTotalCount * 2));   // size of audio data 16-bit
            }
            catch
            {
                Debug.Fail("Failed to write file.");
                return false;
            }

            return true;
        }
    }
}
