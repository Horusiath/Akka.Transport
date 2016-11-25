using System;
using Akka.Actor;

namespace Samples.Server
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var system = ActorSystem.Create("remote-system"))
            {

                Console.ReadLine();
            }
        }
    }
}
