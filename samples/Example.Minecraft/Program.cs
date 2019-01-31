using Example.Minecraft.Net;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Example.Minecraft
{
    class Program
    {
        static void Main(string[] args)
        {
            // create the protosocket server
            ClassicServer server = new ClassicServer(new Uri("tcp://0.0.0.0:25565"));

            // create world
            World world = new World(server);
            world.TickRate = 20;

            // start server and run the game tick loop
            server.Start();
            world.RunAsync().Wait();
        }
    }
}
