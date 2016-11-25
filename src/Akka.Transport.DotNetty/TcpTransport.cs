using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Transport;
using Akka.Util;
using DotNetty.Codecs;
using DotNetty.Common.Utilities;
using DotNetty.Handlers.Tls;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels.Sockets;

namespace Akka.Transport.DotNetty
{
    public class TcpTransport : Remote.Transport.Transport
    {
        public readonly DotNettySettings Settings;

        private readonly IEventLoopGroup serverEventLoopGroup;
        private readonly IEventLoopGroup clientEventLoopGroup;
        private readonly ConcurrentSet<IChannel> connectionGroup;
        private readonly ILoggingAdapter log;

        private volatile Address serverAddress;
        private volatile IChannel serverChannel;
        private readonly TaskCompletionSource<IAssociationEventListener> associationListenerPromise;

        public TcpTransport(ActorSystem system) : this(system, DotNettySettings.Create(system))
        {
        }

        public TcpTransport(ActorSystem system, Config config) : this(system, DotNettySettings.Create(config))
        {
        }

        public TcpTransport(ActorSystem system, DotNettySettings settings)
        {
            this.System = system;
            this.Settings = settings;
            this.log = Logging.GetLogger(System, GetType());
            this.serverEventLoopGroup = new MultithreadEventLoopGroup(this.Settings.ServerSocketWorkerPoolSize);
            this.clientEventLoopGroup = new MultithreadEventLoopGroup(this.Settings.ClientSocketWorkerPoolSize);
            this.connectionGroup = new ConcurrentSet<IChannel>();
            this.associationListenerPromise = new TaskCompletionSource<IAssociationEventListener>();
        }

        public override string SchemeIdentifier => (Settings.EnableSsl ? "Ssl." : string.Empty) + (Settings.TransportMode.ToString().ToLower());

        public override long MaximumPayloadBytes => Settings.MaxFrameSize;
        
        public override async Task<Tuple<Address, TaskCompletionSource<IAssociationEventListener>>> Listen()
        {
            IPAddress ip;

            var listenAddress = IPAddress.TryParse(this.Settings.Host, out ip)
                ? (EndPoint)new IPEndPoint(ip, this.Settings.Port)
                : new DnsEndPoint(this.Settings.Host, this.Settings.Port);

            try
            {
                var channel = await ServerFactory().BindAsync(listenAddress);
                channel.Configuration.AutoRead = false;
                this.TryAddChannel(channel);
                this.serverChannel = channel;
                var address = Helpers.MapSocketToAddress(
                    socketAddress: channel.LocalAddress as IPEndPoint, 
                    schemeIdentifier: SchemeIdentifier, 
                    systemName: System.Name,
                    hostName: Settings.Host);

                if (address == null) throw new ConfigurationException($"Unknown local address type {channel.LocalAddress}");

                this.serverAddress = address;
#pragma warning disable 4014 // we WANT this task to run without waiting
                this.associationListenerPromise.Task.ContinueWith(result => 
                    this.serverChannel.Configuration.AutoRead = true,
                    TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);
#pragma warning restore 4014

                return Tuple.Create(address, this.associationListenerPromise);
            }
            catch (Exception cause)
            {
                this.log.Error(cause, $"Failed to bind to {listenAddress}. Shutting down DotNetty transport.");

                try
                {
                    await Shutdown();
                }
                catch (Exception) { /* swallow */ }

                ExceptionDispatchInfo.Capture(cause).Throw();
                return null;
            }
        }

        public override bool IsResponsibleFor(Address remote) => true;

        public override async Task<AssociationHandle> Associate(Address remoteAddress)
        {
            if (!this.serverChannel.Open)
                throw new ChannelException("Transport is not open");

            var clientBootstrap = ClientFactory(remoteAddress);
            var endpoint = Helpers.ToEndpoint(remoteAddress);
            var channel = await clientBootstrap.ConnectAsync(endpoint);
            var handler = (TcpClientHandler)channel.Pipeline.Last();

            return await handler.StatusTask;
        }

        public override async Task<bool> Shutdown()
        {
            try
            {
                var closing = new List<Task>();
                foreach (var channel in this.connectionGroup)
                {
                    closing.Add(channel.CloseAsync());
                }

                var allClosing = Task.WhenAll(closing);
                await allClosing;

                var serverClosing = this.serverChannel?.CloseAsync() ?? TaskEx.Completed;
                await serverClosing;

                return allClosing.IsCompleted && serverClosing.IsCompleted;
            }
            finally
            {
                this.connectionGroup.Clear();
                await this.clientEventLoopGroup.ShutdownGracefullyAsync();
                await this.serverEventLoopGroup.ShutdownGracefullyAsync();
            }
        }

        public bool TryAddChannel(IChannel channel) => this.connectionGroup.TryAdd(channel);

        public bool TryRemoveChannel(IChannel channel) => this.connectionGroup.TryRemove(channel);

        #region private

        private void SetInitialChannelPipeline(IChannel channel)
        {
            var pipeline = channel.Pipeline;

            if (Settings.TransportMode == TransportMode.Tcp)
            {
                pipeline.AddLast("FrameDecoder", new LengthFieldBasedFrameDecoder((int)MaximumPayloadBytes, 0, 4, 0, 4));
                pipeline.AddLast("FrameEncoder", new LengthFieldPrepender(4, false));
            }
        }

        private void SetClientPipeline(IChannel channel, Address remoteAddress)
        {
            if (Settings.EnableSsl)
            {
                var certificate = Settings.Ssl.Certificate;
                var host = certificate.GetNameInfo(X509NameType.DnsName, false);
                
                channel.Pipeline.AddLast("tls", TlsHandler.Client(host, certificate));
            }

            SetInitialChannelPipeline(channel);
            var pipeline = channel.Pipeline;

            if (Settings.TransportMode == TransportMode.Tcp)
            {
                var handler = new TcpClientHandler(this, Logging.GetLogger(this.System, typeof(TcpClientHandler)), remoteAddress);
                pipeline.AddLast("clientHandler", handler);
            }
        }

        private void SetServerPipeline(IChannel channel)
        {
            if (Settings.EnableSsl)
            {
                channel.Pipeline.AddLast("tls", TlsHandler.Server(Settings.Ssl.Certificate));
            }

            SetInitialChannelPipeline(channel);
            var pipeline = channel.Pipeline;

            if (Settings.TransportMode == TransportMode.Tcp)
            {
                var handler = new TcpServerHandler(this, Logging.GetLogger(this.System, typeof(TcpServerHandler)), associationListenerPromise.Task);
                pipeline.AddLast("clientHandler", handler);
            }
        }

        private Bootstrap ClientFactory(Address remoteAddress)
        {
            if (this.Settings.TransportMode != TransportMode.Tcp) 
                throw new NotSupportedException("Currently DotNetty client supports only TCP tranport mode.");

            var addressFamily = this.Settings.DnsUseIpv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;

            var bootstrap = new Bootstrap()
                .Group(clientEventLoopGroup)
                .Option(ChannelOption.SoReuseaddr, this.Settings.TcpReuseAddress)
                .Option(ChannelOption.SoKeepalive, this.Settings.TcpKeepAlive)
                .Option(ChannelOption.TcpNodelay, this.Settings.TcpNoDelay)
                .Option(ChannelOption.ConnectTimeout, this.Settings.ConnectTimeout)
                .Option(ChannelOption.AutoRead, false)
                .ChannelFactory(() => this.Settings.EnforceIpFamily 
                    ? new TcpSocketChannel(addressFamily)
                    : new TcpSocketChannel())
                .Handler(new ActionChannelInitializer<TcpSocketChannel>(channel => SetClientPipeline(channel, remoteAddress)));

            if (this.Settings.ReceiveBufferSize.HasValue) bootstrap.Option(ChannelOption.SoRcvbuf, this.Settings.ReceiveBufferSize.Value);
            if (this.Settings.SendBufferSize.HasValue) bootstrap.Option(ChannelOption.SoSndbuf, this.Settings.SendBufferSize.Value);
            if (this.Settings.WriteBufferHighWaterMark.HasValue) bootstrap.Option(ChannelOption.WriteBufferHighWaterMark, this.Settings.WriteBufferHighWaterMark.Value);
            if (this.Settings.WriteBufferLowWaterMark.HasValue) bootstrap.Option(ChannelOption.WriteBufferLowWaterMark, this.Settings.WriteBufferLowWaterMark.Value);

            return bootstrap;
        }

        private ServerBootstrap ServerFactory()
        {
            if (this.Settings.TransportMode != TransportMode.Tcp)
                throw new NotSupportedException("Currently DotNetty server supports only TCP tranport mode.");

            var addressFamily = this.Settings.DnsUseIpv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;

            var bootstrap = new ServerBootstrap()
                .Group(serverEventLoopGroup)
                .Option(ChannelOption.SoReuseaddr, this.Settings.TcpReuseAddress)
                .Option(ChannelOption.SoKeepalive, this.Settings.TcpKeepAlive)
                .Option(ChannelOption.TcpNodelay, this.Settings.TcpNoDelay)
                .Option(ChannelOption.AutoRead, false)
                .Option(ChannelOption.SoBacklog, this.Settings.Backlog)
                .ChannelFactory(() => Settings.EnforceIpFamily
                    ? new TcpServerSocketChannel(addressFamily) 
                    : new TcpServerSocketChannel())
                .ChildHandler(new ActionChannelInitializer<TcpSocketChannel>(SetServerPipeline));

            if (this.Settings.ReceiveBufferSize.HasValue) bootstrap.Option(ChannelOption.SoRcvbuf, this.Settings.ReceiveBufferSize.Value);
            if (this.Settings.SendBufferSize.HasValue) bootstrap.Option(ChannelOption.SoSndbuf, this.Settings.SendBufferSize.Value);
            if (this.Settings.WriteBufferHighWaterMark.HasValue) bootstrap.Option(ChannelOption.WriteBufferHighWaterMark, this.Settings.WriteBufferHighWaterMark.Value);
            if (this.Settings.WriteBufferLowWaterMark.HasValue) bootstrap.Option(ChannelOption.WriteBufferLowWaterMark, this.Settings.WriteBufferLowWaterMark.Value);

            return bootstrap;
        }

        #endregion
    }
}