using System;

namespace JChopper
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class CustomSerializerAttribute : Attribute
    {
        public CustomSerializerAttribute(Type customSerializerType)
        {
            this.CustomSerializerType = customSerializerType;
        }

        public Type CustomSerializerType { get; }
    }
}
