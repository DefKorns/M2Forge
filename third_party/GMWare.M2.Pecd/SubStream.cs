using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GMWare.M2.Pecd
{
    // https://stackoverflow.com/a/40517416
    /// <summary>
    /// Provides a generic view of a sequence of bytes into a section of a <see cref="Stream"/>.
    /// </summary>
    public class SubStream : Stream
    {
        private Stream baseStream;
        private readonly long length;
        private long position;
        readonly long startPos;

        /// <inheritdoc/>
        public SubStream(Stream baseStream, long offset, long length)
        {
            if (baseStream == null) throw new ArgumentNullException("baseStream");
            if (!baseStream.CanRead) throw new ArgumentException("can't read base stream");
            if (offset < 0) throw new ArgumentOutOfRangeException("offset");

            this.baseStream = baseStream;
            this.length = length;
            startPos = offset;

            if (baseStream.CanSeek)
            {
                baseStream.Seek(offset, SeekOrigin.Begin);
            }
            else
            { // read it manually...
                Consume(offset);
            }
        }

        void Consume(long length)
        {
            const int BUFFER_SIZE = 512;
            byte[] buffer = new byte[BUFFER_SIZE];
            while (length > 0)
            {
                int read = baseStream.Read(buffer, 0, length < BUFFER_SIZE ? (int)length : BUFFER_SIZE);
                length -= read;
            }
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            CheckDisposed();
            long remaining = length - position;
            if (remaining <= 0) return 0;
            if (remaining < count) count = (int)remaining;
            int read = baseStream.Read(buffer, offset, count);
            position += read;
            return read;
        }
        private void CheckDisposed()
        {
            if (baseStream == null) throw new ObjectDisposedException(GetType().Name);
        }
        /// <inheritdoc/>
        public override long Length
        {
            get { CheckDisposed(); return length; }
        }
        /// <inheritdoc/>
        public override bool CanRead
        {
            get { CheckDisposed(); return true; }
        }
        /// <inheritdoc/>
        public override bool CanWrite
        {
            get { CheckDisposed(); return false; }
        }
        /// <inheritdoc/>
        public override bool CanSeek
        {
            get { CheckDisposed(); return false; }
        }
        /// <inheritdoc/>
        public override long Position
        {
            get
            {
                CheckDisposed();
                return position;
            }
            set { throw new NotSupportedException(); }
        }
        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (baseStream.CanSeek)
            {
                switch (origin)
                {
                    case SeekOrigin.Begin:
                        position = baseStream.Seek(startPos + offset, SeekOrigin.Begin) - startPos;
                        break;
                    case SeekOrigin.Current:
                        position = baseStream.Seek(offset, SeekOrigin.Current) - startPos;
                        break;
                    case SeekOrigin.End:
                        position = baseStream.Seek(startPos + length + offset, SeekOrigin.Begin) - startPos;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(origin));
                }
            }
            else
            {
                switch (origin)
                {
                    case SeekOrigin.Current:
                        Consume(offset);
                        position += offset;
                        break;
                    default:
                        throw new NotSupportedException();
                }
            }
            return position;
        }
        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
        /// <inheritdoc/>
        public override void Flush()
        {
            CheckDisposed(); baseStream.Flush();
        }
        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                if (baseStream != null)
                {
                    try { baseStream.Dispose(); }
                    catch { }
                    baseStream = null;
                }
            }
        }
        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}
