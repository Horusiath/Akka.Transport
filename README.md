This is a set of experimental libraries, that can be used as an alternative backends for Akka.NET remoting layer.

The goal is to test as much of the followin libraries:

- [DotNetty](https://github.com/Azure/DotNetty) as the closest to existing akka default (Helios). Potentially drop-in replacement.
- [gRPC](https://github.com/grpc/grpc/tree/master/src/csharp) in streaming mode. Usefull as it already makes use of ByteString and Google Protocol Buffers, which are both used nativelly by Akka.Remote.
- [System.IO.Pipelines](https://github.com/dotnet/corefxlab/tree/master/src/System.IO.Pipelines) - there are various socket lib implementations including: libuv, CLR sockets, Windows RIO.