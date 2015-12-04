using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Formatting;
using System.Text.Utf8;
using System.Threading.Tasks;
using JChopper;

namespace JChopper.Test
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(JsonSerializer.Default.Serialize(new TestClass
            {
                X = new Utf8String("這いよる混沌のようなホモ怖い。\r\n\0"),
                Y = 2
            }).ToString());
            Console.ReadLine();
        }
    }

    class TestClass
    {
        public Utf8String X { get; set; }

        [CustomSerializer(typeof(StringifyConverter))]
        public int Y { get; set; }
    }

    class StringifyConverter : ICustomSerializer<int>
    {
        public void Serialize(int obj, IFormatter formatter)
        {
            formatter.Append('"');
            formatter.Append(obj);
            formatter.Append('"');
        }

        public int Deserialize(Utf8String json)
        {
            throw new NotImplementedException();
        }
    }
}
