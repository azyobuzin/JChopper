using System.Text.Formatting;
using System.Text.Utf8;

namespace JChopper
{
    public interface ICustomSerializer<T>
    {
        void Serialize<TFormatter>(T obj, TFormatter formatter) where TFormatter : IFormatter;
        T Deserialize(Utf8String json);
    }
}
