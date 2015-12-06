using System.Text.Formatting;
using System.Text.Utf8;
using JChopper.Writers;

namespace JChopper
{
    public interface ICustomSerializer<T>
    {
        void Serialize(T obj, IWriter writer);
        T Deserialize(Utf8String json);
    }
}
