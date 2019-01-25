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

Overall the library makes use of the concept of peers, which can represent a inbound connection or an outgoing client connection. You can use `Peer.Side` to determine if a peer is a client or server connection. The library provides three ways of receiving packets from the opposing peer, it is safe to use all at once if your application requires.

- `IObserver<TFrame>` subscriptions
- `Peer.Received` event
- `ReceiveAsync` tasks

When a `TFrame` has been decoded by your `IProtocolCoder` implementation, the peer needs to decide which receiever to inform. It prioritises responses to `RequestAsync` calls, followed by `ReceiveAsync` requests, and will not trigger the `Received` event or inform any subscribers if a frame is pulled from the peer this way.

For the `Received` event, the event handler will always be called when no `ReceiveAsync` operation is pending. Optionally the `ReceivedEventArgs.Handled` property may be set to true to indicate that the frame should not be passed to any subscriptions.

Finally if all other handlers have been called without conflict, any `IObserver<TFrame>` subscriptions will be called.

### Queueing

In many scenarios creating an asyncronous operation and waiting for every packet to be sent is not ideal, for these use cases you can use the `ProtocolPeer.Queue` and `ProtocolPeer.QueueAsync` methods.

Queueing a packet does not provide any guarentee it will be sent in a timely fashion, it is up to you to call `ProtocolPeer.SendAsync`/`ProtocolPeer.FlushAsync` for any queued packets to be sent. If you want to queue packets but need to confirm or wait until they have been sent, you can use the `ProtocolPeer.QueueAsync` method.

This allows you to batch multiple frames together while still waiting until they are sent.

```csharp
Task message1 = peer.QueueAsync(new ChatMessage() { Text = "I like books" });
Task message2 = peer.QueueAsync(new ChatMessage() { Text = "I like books alot" });
Task message3 = peer.QueueAsync(new ChatMessage() { Text = "I like eBooks too" });

// you must call peer.SendAsync for these messages to be sent
await Task.WhenAll(message1, message2, message3);
``` 

### Coders

In the newer version of ProtoSocket, coders are implemented using `System.IO.Pipelines`. The same high-performance library powering ASP.NET Kestrel.

You can find a great tutorial on the .NET Blog [here](https://blogs.msdn.microsoft.com/dotnet/2018/07/09/system-io-pipelines-high-performance-io-in-net/). Examples are available inside the repository, [ChatCoder.cs](samples/Example.Chat/ChatCoder.cs) and [ClassicCoder.cs](samples/Example.Minecraft/Net/ClassicCoder.cs).

Your implementation simply needs to call `PipeReader.TryRead`, processing as much data as possible and either returning a frame (and true), or false to indicate you haven't got a full frame yet. The underlying peer will continually call your read implementation until you are able to output no more frames.

## Contributing

Any pull requests or bug reports are welcome, please try and keep to the existing style conventions and comment any additions.
