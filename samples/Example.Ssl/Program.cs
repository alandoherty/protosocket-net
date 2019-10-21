using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Example.Ssl
{
    class Program
    {
        static Task Main(string[] args)
        {
            Server();
            return ClientAsync();
        }

        static void Server()
        {
            // configure the server
            SslServer server = new SslServer(new X509Certificate2("ssl.p12"));
            server.Configure("tcp://127.0.0.1:3001");

            server.Connected += (o, e) => {
                Console.WriteLine($"srv:{e.Peer.RemoteEndPoint}: connected");
            };

            server.Disconnected += (o, e) => {
                Console.WriteLine($"srv:{e.Peer.RemoteEndPoint}: disconnected");
            };

            // start the server
            server.Start();
        }

        static async Task ClientAsync()
        {
            // try and connect three times, on the third time we will show an error
            SslClient client = null;

            for (int i = 0; i < 3; i++) {
                client = new SslClient();

                try {
                    await client.ConnectAsync(new Uri("tcp://127.0.0.1:3001"))
                        .ConfigureAwait(false);
                    break;
                } catch(Exception ex) {
                    if (i == 2) {
                        Console.Error.WriteLine($"client:{ex.ToString()}");
                        return;
                    } else {
                        await Task.Delay(1000)
                            .ConfigureAwait(false);
                    }
                }
            }

            // show a basic read line prompt, sending every frame to the server once enter is pressed
            string line = null;

            do {
                // read a line of data
                Console.Write("> ");
                line = (await Console.In.ReadLineAsync().ConfigureAwait(false)).Trim();

                // send
                await client.SendAsync(line)
                    .ConfigureAwait(false);

                // wait for reply
                await client.ReceiveAsync()
                     .ConfigureAwait(false);
            } while (!line.Equals("exit", StringComparison.OrdinalIgnoreCase));
        }
    }
}
