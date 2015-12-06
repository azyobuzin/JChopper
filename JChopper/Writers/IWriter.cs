using System;
using System.Text.Utf8;

namespace JChopper.Writers
{
    public interface IWriter : IDisposable
    {
        ArraySegment<byte> GetFreeBuffer(int minBytes);
        void CommitBytes(int bytes);
        void Write(byte[] value);
        void Write(byte value);
        void Write(Utf8String value);
    }
}
