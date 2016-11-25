using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Samples.Shared;

namespace Samples.Client
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var system = ActorSystem.Create("system"))
            {
                var aref = system.ActorOf(Props.Create<Greeter>()
                    .WithDeploy(new Deploy(new RemoteScope(Address.Parse("akka.tcp://remote-system@127.0.0.1:2552/")))), "greeter");

                aref.Tell("hello world");

                Console.ReadLine();
            }
        }
    }
}
