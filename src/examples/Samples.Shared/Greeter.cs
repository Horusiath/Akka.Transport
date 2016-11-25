using System;
using Akka.Actor;

namespace Samples.Shared
{
    public class Greeter : ReceiveActor
    {
        public Greeter()
        {
            Receive<string>(msg => Console.WriteLine(msg));
        }
    }
}
