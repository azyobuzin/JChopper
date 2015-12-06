using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Utf8;
using JChopper.Writers;

namespace JChopper
{
    public class JsonSerializer : IJsonSerializer
    {
        protected JsonSerializer() { }

        public static JsonSerializer Default { get; } = new JsonSerializer();

        private readonly ConcurrentDictionary<Type, Delegate> _cache = new ConcurrentDictionary<Type, Delegate>();

        public virtual void Serialize<T>(T obj, IWriter writer)
        {
            var serializer =
                _cache.GetOrAdd(typeof(T), _ => new JsonSerializerBuilder<T>(this).CreateSerializer())
                as SerializationAction<T>;
            serializer(obj, writer);
        }

        public Utf8String Serialize<T>(T obj)
        {
            ArraySegment<byte> result;
            using (var writer = new MemoryWriter())
            {
                this.Serialize(obj, writer);
                result = writer.EndWrite();
            }
            return new Utf8String(result.Array, result.Offset, result.Count);
        }

        public void Serialize<T>(T obj, Stream stream)
        {
            // Dispose to return the buffer.
            using (var writer = new Writers.StreamWriter(stream))
                this.Serialize(obj, writer);
        }
    }

    public interface IJsonSerializer
    {
        void Serialize<T>(T obj, IWriter writer);
    }

    public delegate void SerializationAction<T>(T obj, IWriter writer);
}
