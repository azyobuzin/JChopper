using System;
using System.Text.Formatting;

namespace JChopper
{
    static class JsonSerializerCache<T>
    {
        public static Action<T, IFormatter> Serializer { get; set; }
    }
}
