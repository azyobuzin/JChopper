using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;
using System.Text.Utf8;
using System.Text.Formatting;

namespace JChopper
{
    public class JsonSerializer
    {
        public virtual void Serialize<T, TFormatter>(T obj, TFormatter formatter) where TFormatter : IFormatter
        {
            new JsonSerializerBuilder<T, TFormatter>().GetSerializer().Invoke(obj, formatter);
        }
    }
}
