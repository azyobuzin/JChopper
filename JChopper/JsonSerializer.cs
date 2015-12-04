using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Formatting;
using System.Text.Utf8;

namespace JChopper
{
    public class JsonSerializer : IJsonSerializer
    {
        protected JsonSerializer() { }

        public static JsonSerializer Default { get; } = new JsonSerializer();

        private readonly ConcurrentDictionary<Type, Delegate> cache = new ConcurrentDictionary<Type, Delegate>();

        public virtual void Serialize<T>(T obj, IFormatter formatter)
        {
            var serializer =
                cache.GetOrAdd(typeof(T), _ => new JsonSerializerBuilder<T>(this).CreateSerializer())
                as Action<T, IFormatter>;
            serializer(obj, formatter);
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

    public interface IJsonSerializer
    {
        void Serialize<T>(T obj, IFormatter formatter);
    }
}
