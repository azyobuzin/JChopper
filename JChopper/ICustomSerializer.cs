using System.Text.Formatting;
using System.Text.Utf8;

namespace JChopper
{
    public interface ICustomSerializer<T>
    {
        void Serialize(T obj, IFormatter formatter);
        T Deserialize(Utf8String json);
    }
}
