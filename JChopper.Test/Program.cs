using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using JChopper;

namespace JChopper.Test
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(new JsonSerializer().Serialize(new A { Rec = new A() }).ToString());
            Console.ReadLine();
        }
    }

    class A
    {
        [DataMember(Order = 0)]
        private int X = 1;

        [DataMember(Order = 1)]
        public A Rec;
    }
}
