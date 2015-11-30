using System;
using System.Text.Formatting;
using System.Text.Utf8;

namespace JChopper
{
    internal class Utf8Formatter : IFormatter
    {
        public Utf8Formatter(int initialCapacity)
        {
            this.buffer = new byte[initialCapacity];
            this.count = 0;
        }

        private byte[] buffer;
        private int count;

        public FormattingData FormattingData => FormattingData.InvariantUtf8;

        public Span<byte> FreeBuffer => new Span<byte>(this.buffer, this.count, this.buffer.Length - this.count);

        public void CommitBytes(int bytes)
        {
            this.count += bytes;
        }

        public void ResizeBuffer()
        {
            var oldBuffer = this.buffer;
            if (oldBuffer.Length == 0)
            {
                this.buffer = new byte[2];
                return;
            }
            var newBuffer = new byte[oldBuffer.Length * 2];
            Buffer.BlockCopy(oldBuffer, 0, newBuffer, 0, oldBuffer.Length);
            this.buffer = newBuffer;
        }

        public Utf8String ToUtf8String()
        {
            return new Utf8String(this.buffer, 0, this.count);
        }
    }
}
