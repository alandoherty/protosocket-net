<div align="center">

[![GitHub license](https://img.shields.io/badge/license-MIT-blue.svg?style=flat-square)](https://raw.githubusercontent.com/alandoherty/protosocket-net/master/LICENSE)
[![GitHub issues](https://img.shields.io/github/issues/alandoherty/protosocket-net.svg?style=flat-square)](https://github.com/alandoherty/protosocket-net/issues)
[![GitHub stars](https://img.shields.io/github/stars/alandoherty/protosocket-net.svg?style=flat-square)](https://github.com/alandoherty/protosocket-net/stargazers)
[![GitHub forks](https://img.shields.io/github/forks/alandoherty/protosocket-net.svg?style=flat-square)](https://github.com/alandoherty/protosocket-net/network)
[![GitHub forks](https://img.shields.io/nuget/dt/ProtoSocket.svg?style=flat-square)](https://www.nuget.org/packages/ProtoSocket/)

</div>

# protosocket

A networking library for frame-based, performant asynchronous TCP sockets on .NET Core. Open permissive MIT license and requires a minimum of .NET Standard 1.3.

## Getting Started

[![NuGet Status](https://img.shields.io/nuget/v/ProtoSocket.svg?style=flat-square)](https://www.nuget.org/packages/ProtoSocket/)

You can install the package using either the CLI:

```
dotnet add package ProtoSocket
```

or from the NuGet package manager:

```
Install-Package ProtoSocket
```

### Example

You can find two examples inside the project source code. An implementation of a Minecraft Classic server (very basic), and a basic binary chat server.

In the Minecraft example, you can type `/text Moi moi or /texta Moi moi` for 3D text to appear in your world. 

![Example](docs/img/example_minecraft.png)

## Usage

In ProtoSocket, sockets are represented as peers, which can be either an inbound connection or an outgoing client connection. You can use `Peer.Side` to determine if a peer is a client or server connection. The library provides three ways of receiving packets from the opposing peer, it is safe to use all at once if your application requires.

- `IObserver<TFrame>` subscriptions
- `Peer.Received` event
- `ReceiveAsync` tasks

When a `TFrame` has been decoded by your `IProtocolCoder` implementation, the peer needs to decide which receiever to inform. It prioritises responses to `RequestAsync` calls, followed by `ReceiveAsync` requests, and will not trigger the `Received` event or inform any subscribers if a frame is pulled from the peer this way.

For the `Received` event, the event handler will always be called when no `ReceiveAsync` operation is pending. Optionally the `ReceivedEventArgs.Handled` property may be set to true to indicate that the frame should not be passed to any subscriptions.

Finally if all other handlers have been called without conflict, any `IObserver<TFrame>` subscriptions will be called.

### Coders

In the newer version of ProtoSocket, coders are implemented using `System.IO.Pipelines`. The same high-performance library powering ASP.NET Kestrel.

You can find a great tutorial on the .NET Blog [here](https://blogs.msdn.microsoft.com/dotnet/2018/07/09/system-io-pipelines-high-performance-io-in-net/). Examples are available inside the repository, [ChatCoder.cs](samples/Example.Chat/ChatCoder.cs) and [ClassicCoder.cs](samples/Example.Minecraft/Net/ClassicCoder.cs).

Your implementation simply needs to call `PipeReader.TryRead`, processing as much data as possible and either returning a frame (and true), or false to indicate you haven't got a full frame yet. The underlying peer will continually call your read implementation until you are able to output no more frames.

### Queueing

In many scenarios creating an asynchronous operation and waiting for every packet to be sent is not ideal, for these use cases you can use the `ProtocolPeer.Queue` and `ProtocolPeer.QueueAsync` methods.

Queueing a packet does not provide any guarentee it will be sent in a timely fashion, it is up to you to call `ProtocolPeer.SendAsync`/`ProtocolPeer.FlushAsync` for any queued packets to be sent. If you want to queue packets but need to confirm or wait until they have been sent, you can use the `ProtocolPeer.QueueAsync` method.

This allows you to batch multiple frames together while still waiting until they are sent, the order will be retained. While the peer is thread-safe you will need to perform your own synchronization while queueing/sending to guarentee the order.

```csharp
Task message1 = peer.QueueAsync(new ChatMessage() { Text = "I like books" });
Task message2 = peer.QueueAsync(new ChatMessage() { Text = "I like books alot" });
Task message3 = peer.QueueAsync(new ChatMessage() { Text = "I like eBooks too" });

// you can either call peer.FlushAsync elsewhere or wait until the next call to peer.SendAsync(TFrame/TFrame[]/etc)
await Task.WhenAll(message1, message2, message3);
``` 

### Modes

In the newer versions of ProtoSocket you can now create peers in either `Active` or `Passive` mode. In Active mode the peers act 

### Upgrading/SSL

In many scenarios you will want to perform an upgrade of the underlying transport connection, for example to support TLS/SSL connections. ProtoSocket provides an easy means of doing this via the `IProtocolUpgrader<TFrame>` interface. Upgrading a connection for versioned protocols, or changing the frame type is not supported and not the target use case of the upgrade API.

To upgrade the connection to SSL for example, use the pre-built `SslUpgrader` class. Note that flushing or sending frames on the peer will trigger an `InvalidOperationException`. You can queue frames however.

```
SslUpgrader upgrader = new SslUpgrader("www.google.com");
upgrader.Protocols = SslProtocols.Tls | SslProtocols.Tls11;
await peer.UpgradeAsync(upgrader);

await peer.SendAsync(new ChatMessage() { Text = "Encrypted chat message!" });
```

You can also upgrade explicitly after connecting, preventing the underlying read loop from accidently interpreting upgraded traffic.

```
ProtocolClient client = new ProtocolClient(new MyCoder(), ProtocolMode.Passive);
SslUpgrader upgrader = new SslUpgrader("www.google.com");
upgrader.Protocols = SslProtocols.Tls | SslProtocols.Tls11;

await client.UpgradeAsync(upgrader);
client.Mode = ProtocolMode.Active;
```

### Filters

You can selectively decline incoming connections by adding a filter to the `ProtocolServer` object. If filtered, the connection will not be added to the server and the socket will be closed instantly.

A filter can be created manually by implementing `IConnectionFilter`, or you can use the premade classes `AsyncConnectionFilter`/`ConnectionFilter` which accept a delegate as their constructor.

```csharp
server.Filter = new ConnectionFilter(ctx => ((IPEndPoint)ctx.RemoteEndPoint).Address != IPAddress.Parse("192.168.0.2"));
```

You can optionally use the asyncronous filter, which will allow you to accept other connections in the background while processing your filter.

```csharp
server.Filter = new AsyncConnectionFilter(async (ctx, ct) => {
	await Task.Delay(3000);
	return false;
});
```

### Reusing Frames

In some cases you may want to pool/optimise your frames in such a way that managed buffers are reused between frames. You can achieve this by implementing `IDisposable`, which will result in `Dispose` being called automatically when the frame is flushed to the opposing peer. Allowing you to take back any resources for use by other frames. The peer will not dispose a frame after receiving, it is up to you to call `Dispose` when you have processed the frame.

```csharp
public struct PooledFrame : IDisposable
{
	public byte[] Buffer { get; set; }
	public int Size { get; set; }

	public void Dispose() {
		ArrayPool<byte>.Shared.Return(Buffer);
	}

	public PooledFrame(int bufferSize) {
		Buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
		Size = bufferSize;
	}
}
```

## Contributing

Any pull requests or bug reports are welcome, please try and keep to the existing style conventions and comment any additions.
