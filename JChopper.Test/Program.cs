using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Formatting;
using System.Text.Utf8;
using System.Threading.Tasks;
using JChopper;
using JChopper.Writers;

namespace JChopper.Test
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(JsonSerializer.Default.Serialize(new TestClass
            {
                X = new Utf8String("這いよる混沌のようなホモ怖い。\r\n\0"),
                Y = 2,
                Z = "わかり手の手✋\r\n"
            }).ToString());
            Console.ReadLine();
        }
    }

    class TestClass
    {
        public Utf8String X { get; set; }
        public int Y { get; set; }
        public string Z { get; set; }
    }
}
