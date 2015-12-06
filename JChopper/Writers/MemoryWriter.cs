using System;
using System.Buffers;
using System.Text.Utf8;

namespace JChopper.Writers
{
    public class MemoryWriter : IWriter
    {
        private static readonly ManagedBufferPool<byte> pool = ManagedBufferPool<byte>.SharedByteBufferPool;

        private byte[] _buffer;
        private int _count;

        public MemoryWriter()
        {
            this._buffer = pool.RentBuffer(64);
        }

        private void RequireBuffer(int minBytes)
        {
            var requiredSize = this._count + minBytes;
            if (this._buffer.Length < requiredSize)
                pool.EnlargeBuffer(ref this._buffer, requiredSize);
        }

        public ArraySegment<byte> GetFreeBuffer(int minBytes)
        {
            this.RequireBuffer(minBytes);
            return new ArraySegment<byte>(this._buffer, this._count, this._buffer.Length - this._count);
        }

        public void CommitBytes(int bytes)
        {
            this._count += bytes;
        }

        public void Write(byte[] value)
        {
            this.RequireBuffer(value.Length);
            Buffer.BlockCopy(value, 0, this._buffer, this._count, value.Length);
            this._count += value.Length;
        }

        public void Write(byte value)
        {
            if (this._buffer.Length <= this._count)
                pool.EnlargeBuffer(ref this._buffer, this._count + 1);

            this._buffer[this._count++] = value;
        }

        public void Write(Utf8String value)
        {
            this.RequireBuffer(value.Length);
            value.CopyTo(this._buffer.Slice(this._count));
            this._count += value.Length;
        }

        public ArraySegment<byte> EndWrite()
        {
            var result = new ArraySegment<byte>(this._buffer, 0, this._count);

            // Create a dummy buffer for ReturnBuffer
            var tmp = new byte[this._buffer.Length];
            pool.ReturnBuffer(ref tmp);
            this._buffer = null;

            return result;
        }

        public ArraySegment<byte> GetBuffer()
        {
            return new ArraySegment<byte>(this._buffer, 0, this._count);
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
