using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using ProtoSocket.Upgraders;

namespace Example.Chat
{
    class Program
    {
        static void Main(string[] args)
        {
            Task.WhenAny(RunServerAsync(args),
            RunClientAsync(new string[] { "", "Kevin Bacon" })).Wait();

            if (args.Length == 0)
                Console.Error.WriteLine("Example.Chat.exe <server|client> [name]");
            else if (args[0].Equals("server", StringComparison.CurrentCultureIgnoreCase))
                RunServerAsync(args).Wait();
            else if (args[0].Equals("client", StringComparison.CurrentCultureIgnoreCase))
                RunClientAsync(args).Wait();
        }

        static Task RunServerAsync(string[] args) {
            ChatServer server = new ChatServer();
            server.Configure(new Uri("tcp://127.0.0.1:6060"));

            // setup events
            server.Connected += async (s, e) => {
                Console.WriteLine("{0} connected", e.Peer.RemoteEndPoint);
                
                ChatMessage joinMsg = new ChatMessage() {
                    Name = "",
                    Text = e.Peer.RemoteEndPoint + " joined the conversation"
                };

                foreach(ChatConnection conn in server.Connections) {
                    if (conn != e.Peer)
                        try {
                            await conn.SendAsync(joinMsg);
                        } catch (Exception) { }
                }

                e.Peer.Received += async (ss, ee) => {
                    if (ee.Frame.Text == "/list") {
                        string[] ips = server.Connections.Select(c => "\t" + c.RemoteEndPoint.ToString()).ToArray();

                        await e.Peer.SendAsync(new ChatMessage() {
                            Name = "",
                            Text = string.Format("{0} clients\n{1}", ips.Length, string.Join("\n", ips))
                        });
                    } else {
                        foreach (ChatConnection conn in server.Connections) {
                            if (conn != e.Peer)
                                try {
                                    await conn.SendAsync(ee.Frame);
                                } catch (Exception) { }
                        }
                    }
                };
            };

            server.Disconnected += async (s, e) => {
                ChatMessage joinMsg = new ChatMessage() {
                    Name = "",
                    Text = e.Peer.RemoteEndPoint + " left the conversation"
                };

                foreach (ChatConnection conn in server.Connections) {
                    if (conn != e.Peer)
                        try {
                            await conn.SendAsync(joinMsg);
                        } catch (Exception) { }
                }

                Console.WriteLine("{0} disconnected", e.Peer.RemoteEndPoint);
            };

            // start
            server.Start();

            Console.WriteLine("started chat server");

            // wait forever
            return Task.Delay(Timeout.InfiniteTimeSpan);
        }

        static async Task RunClientAsync(string[] args) {
            if (args.Length < 2) {
                Console.Error.WriteLine("Example.Chat.exe client <name>");
                return;
            }

            string name = args[1];

            // connect
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            while (true) {
                Console.WriteLine("Connecting: {0}ms", stopwatch.ElapsedMilliseconds);
                ChatClient client = new ChatClient();
                await client.ConnectAsync(new Uri("tcp://localhost:6060")).ConfigureAwait(false);
                Console.WriteLine("Connected: {0}ms", stopwatch.ElapsedMilliseconds);
                /*
                client.Received += (s, e) => {
                    if (e.Frame.Name != string.Empty)
                        Console.WriteLine("{0}: {1}", e.Frame.Name, e.Frame.Text);
                    else
                        Console.WriteLine("{0}", e.Frame.Text);
                };
                */
                Console.WriteLine("Sending: {0}ms", stopwatch.ElapsedMilliseconds);
                await client.SendAsync(new ChatMessage() {
                    Name = name,
                    Text = "Potato"
                }).ConfigureAwait(false);
                Console.WriteLine("Closing: {0}ms", stopwatch.ElapsedMilliseconds);
                await client.CloseAsync("Potato").ConfigureAwait(false);
                Console.WriteLine("Closed: {0}ms", stopwatch.ElapsedMilliseconds);
            }

            /*
            // loop
            Console.WriteLine("connected to {0}, type /quit to exit");

            while(true) {
                // read line
                string line = await Console.In.ReadLineAsync();

                if (line == "/quit")
                    return;
                else {
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();

                    Task[] tasks = new Task[128];

                    for (int i = 0; i < tasks.Length; i++) {
                        tasks[i] = client.SendAsync(new ChatMessage() {
                            Name = name,
                            Text = line
                        });
                    }

                    await Task.WhenAll(tasks);

                    Console.WriteLine("elapsed {0}ms", stopwatch.ElapsedMilliseconds);
                }
            }
            */
        }
    }
}
