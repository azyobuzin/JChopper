using System.Buffers;
using System.IO;
using System.Text.Formatting;
using System.Text.Utf8;

namespace JChopper
{
    public class JsonSerializer
    {
        public virtual void Serialize<T>(T obj, IFormatter formatter)
        {
            new JsonSerializerBuilder<T>().GetSerializer().Invoke(obj, formatter);
        }

        public Utf8String Serialize<T>(T obj)
        {
            var formatter = new Utf8Formatter(100);
            this.Serialize(obj, formatter);
            return formatter.ToUtf8String();
        }

        public void Serialize<T>(T obj, Stream stream)
        {
            // Dispose to return the buffer.
            using (var formatter = new StreamFormatter(stream, FormattingData.InvariantUtf8, ManagedBufferPool<byte>.SharedByteBufferPool))
                this.Serialize(obj, formatter);
        }
    }
}
