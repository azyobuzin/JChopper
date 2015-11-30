using System;
using System.Text.Formatting;

namespace JChopper
{
    static class JsonSerializerCache<T, TFormatter> where TFormatter : IFormatter
    {
        public static Action<T, TFormatter> Serializer { get; set; }
    }
}
