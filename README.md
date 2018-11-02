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

## Contributing

Any pull requests or bug reports are welcome, please try and keep to the existing style conventions and comment any additions.