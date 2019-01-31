using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ProtoSocket;
using ProtoSocket.Upgraders;

namespace Example.Chat
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length == 0)
                Console.Error.WriteLine("Example.Chat.exe <server|client> [name]");
            else if (args[0].Equals("server", StringComparison.CurrentCultureIgnoreCase))
                await RunServerAsync(args);
            else if (args[0].Equals("client", StringComparison.CurrentCultureIgnoreCase))
                await RunClientAsync(args);
        }

        static Task RunServerAsync(string[] args) {
            ChatServer server = new ChatServer();
            server.Configure(new Uri("tcp://0.0.0.0:6060"));

            // setup events
            server.Connected += async (s, e) => {
                Console.WriteLine("{0} connected", e.Peer.RemoteEndPoint);

                // create join frame
                ChatFrame joinMsg = new ChatFrame() {
                    Name = "",
                    Message = $"{e.Peer.RemoteEndPoint} joined the conversation"
                };

                // send to all other peers
                try {
                    await server.SendAsync(joinMsg, c => c != e.Peer);
                } catch (Exception) { }
                
                e.Peer.Received += async (ss, ee) => {
                    if (string.IsNullOrEmpty(ee.Frame.Name))
                        return;

                    if (ee.Frame.Message == "#list") {
                        string[] ips = server.Connections.Select(c => $"\t{c.Name} ({c.RemoteEndPoint})").ToArray();

                        await e.Peer.SendAsync(new ChatFrame() {
                            Name = "",
                            Message = string.Format("{0} clients\n{1}", ips.Length, string.Join("\n", ips))
                        }).ConfigureAwait(false);
                    } else if (ee.Frame.Message == "#stats") {
                        e.Peer.GetStatistics(out PeerStatistics stats);
                        StringBuilder statsBuilder = new StringBuilder();
                        statsBuilder.AppendLine($"Frames In: {stats.FramesIn}");
                        statsBuilder.AppendLine($"Frames Out: {stats.FramesOut}");
                        statsBuilder.AppendLine($"Bytes In: {stats.BytesIn}");
                        statsBuilder.AppendLine($"Bytes Out: {stats.BytesOut}");
                        statsBuilder.AppendLine($"Elapsed: {Math.Round(stats.AliveSpan.TotalSeconds, 2)}");
                        statsBuilder.Append($"Mode: {e.Peer.Mode}");

                        await e.Peer.SendAsync(new ChatFrame() {
                            Name = "",
                            Message = statsBuilder.ToString()
                        });
                    } else {
                        // send to all other peers
                        try {
                            await server.SendAsync(ee.Frame, c => c != e.Peer);
                        } catch (Exception) { }
                    }
                };
            };

            server.Disconnected += async (s, e) => {
                ChatFrame leaveMsg = new ChatFrame() {
                    Name = "",
                    Message = $"{e.Peer.RemoteEndPoint} left the conversation"
                };

                // send to all other peers
                try {
                    await server.SendAsync(leaveMsg, c => c != e.Peer);
                } catch (Exception) { }

                Console.WriteLine($"{e.Peer.RemoteEndPoint} disconnected due to {e.Peer.CloseReason}{(e.Peer.CloseException == null ? "" : $" {e.Peer.CloseException.ToString()}")}");
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

            // create chat client
            ChatClient client = new ChatClient();
            await client.ConnectAsync(new Uri("tcp://localhost:6060")).ConfigureAwait(false);

            client.Received += (o, e) => {
                Console.WriteLine($"{(string.IsNullOrEmpty(e.Frame.Name) ? "" : $"{e.Frame.Name}: ")}{e.Frame.Message}");
            };

            // loop
            Console.WriteLine($"connected to {client.RemoteEndPoint}, type /quit to exit");

            // performance test variables
            string perfStr = Encoding.ASCII.GetString(new byte[64000]);
            Stopwatch perfStopwatch = new Stopwatch();
            long perfSent = -1;

            while (true) {
                // performance test
                if (perfSent > -1) {
                    if (perfStopwatch.Elapsed >= TimeSpan.FromSeconds(1)) {
                        perfStopwatch.Restart();
                        client.GetStatistics(out PeerStatistics stats);

                        Console.WriteLine($"Sending at {Math.Round(((stats.BytesOut - perfSent) / 1024.0f) / 1024.0f, 2)}/MBs");
                        perfSent = stats.BytesOut;
                    }

                    client.Queue(new ChatFrame() { Message = perfStr, Name = "Potato" });
                    client.Queue(new ChatFrame() { Message = perfStr, Name = "Potato" });
                    client.Queue(new ChatFrame() { Message = perfStr, Name = "Potato" });
                    
                    await client.FlushAsync();
                    continue;
                }

                // read line
                string line = await Console.In.ReadLineAsync();

                if (line == "/quit")
                    return;
                else if (line == "/perf") {
                    perfSent = 0;
                    perfStopwatch.Start();
                }
                else if (line == "/stats") {
                    client.GetStatistics(out PeerStatistics stats);
                    StringBuilder statsBuilder = new StringBuilder();
                    statsBuilder.AppendLine($"Frames In: {stats.FramesIn}");
                    statsBuilder.AppendLine($"Frames Out: {stats.FramesOut}");
                    statsBuilder.AppendLine($"Bytes In: {stats.BytesIn}");
                    statsBuilder.AppendLine($"Bytes Out: {stats.BytesOut}");
                    statsBuilder.AppendLine($"Elapsed: {Math.Round(stats.AliveSpan.TotalSeconds, 2)}");
                    statsBuilder.AppendLine($"Mode: {client.Mode}");

                    Console.Write(statsBuilder.ToString());
                } else {
                    await client.SendAsync(new ChatFrame() {
                        Name = name,
                        Message = line
                    });
                }
            }
        }
    }
}
