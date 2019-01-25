using ProtoSocket.Filters;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Example.Line
{
    class ConsoleObserver : IObserver<string>
    {
        public void OnCompleted() {
        }

        public void OnError(Exception error) {
        }

        public void OnNext(string value) {
            Console.WriteLine(value);
        }
    }

    class Program
    {
        static async Task Main(string[] args) {
            LineServer server = new LineServer();
            server.Configure("tcp://0.0.0.0:6060");
            server.Start();
            
            server.Connected += async (o, e) => {
                e.Peer.Subscribe(new ConsoleObserver());
            };

            while(true) {
                var conns = server.Connections;

                foreach(var c in conns) {
                    Console.WriteLine($"IsConnected: {c.IsConnected} State: {c.State} CR: {c.CloseReason} CE: {(c.CloseException == null ? "null" : c.CloseException.Message)}");
                }

                await Task.Delay(2000);
            }

            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
    }
}
