using System;
using System.Buffers;
using System.IO;
using System.Text.Utf8;

namespace JChopper.Writers
{
    public class StreamWriter : IWriter
    {
        private static readonly ManagedBufferPool<byte> pool = ManagedBufferPool<byte>.SharedByteBufferPool;

        private readonly Stream _stream;
        private byte[] _buffer;

        public StreamWriter(Stream stream)
        {
            this._stream = stream;
        }

        public ArraySegment<byte> GetFreeBuffer(int minBytes)
        {
            if (this._buffer == null)
                this._buffer = pool.RentBuffer(minBytes);
            else if (this._buffer.Length < minBytes)
                pool.EnlargeBuffer(ref this._buffer, minBytes);
            return new ArraySegment<byte>(this._buffer);
        }

        public void CommitBytes(int bytes)
        {
            this._stream.Write(this._buffer, 0, bytes);
        }

        public void Write(byte value)
        {
            this._stream.WriteByte(value);
        }

        public void Write(byte[] value)
        {
            this._stream.Write(value, 0, value.Length);
        }

        public void Write(Utf8String value)
        {
            this.Write(value.CopyBytes());
        }

        public void Dispose()
        {
            if (this._buffer != null)
            {
                pool.ReturnBuffer(ref this._buffer);
                this._buffer = null;
            }
        }
    }
}
