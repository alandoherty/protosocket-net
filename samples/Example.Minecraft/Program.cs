using System;
using System.Threading;

namespace Example.Minecraft
{
    class Program
    {
        static void Main(string[] args)
        {
            // create world
            World world = new World();

            // create server and start
            ClassicServer server = new ClassicServer(world, new Uri("tcp://0.0.0.0:25565"));
            server.Start();

            // wait forever
            while (true) Thread.Sleep(1000);
        }
    }
}
