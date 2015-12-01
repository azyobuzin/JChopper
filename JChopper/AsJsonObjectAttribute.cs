using System;

namespace JChopper
{
    /// <summary>
    /// Specifies that the type should be serialized as a JSON object even if it implements <see cref="System.Collections.IEnumerable"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class AsJsonObjectAttribute : Attribute
    {
    }
}
