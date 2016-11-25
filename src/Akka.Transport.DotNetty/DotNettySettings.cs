using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Util;

namespace Akka.Transport.DotNetty
{
    public sealed class DotNettySettings
    {
        public static DotNettySettings Create(ActorSystem system)
        {
            return Create(system.Settings.Config.GetConfig("akka.remote.dot-netty.tcp"));
        }

        public static DotNettySettings Create(Config config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config), "DotNetty HOCON config was not found (default path: `akka.remote.dot-netty`)");

            var transportMode = config.GetString("transport-protocol", "tcp").ToLower();
            var ip = config.GetString("hostname", "0.0.0.0");
            return new DotNettySettings(
                transportMode: transportMode == "tcp" ? TransportMode.Tcp : TransportMode.Udp,
                enableSsl: config.GetBoolean("enable-Ssl", false),
                connectTimeout: config.GetTimeSpan("connection-timeout", TimeSpan.FromSeconds(15)),
                host: ip,
                port: config.GetInt("port", 2552),
                serverSocketWorkerPoolSize: ComputeWorkerPoolSize(config.GetConfig("server-socket-worker-pool")),
                clientSocketWorkerPoolSize: ComputeWorkerPoolSize(config.GetConfig("client-socket-worker-pool")),
                maxFrameSize: ToNullableInt(config.GetByteSize("maximum-frame-size")) ?? 128000,
                ssl: config.HasPath("ssl") ? SslSettings.Create(config.GetConfig("ssl")) : SslSettings.Empty,
                dnsUseIpv6: config.GetBoolean("dns-use-ipv6", false),
                tcpReuseAddress: config.GetBoolean("tcp-reuse-addr", true),
                tcpKeepAlive: config.GetBoolean("tcp-keepalive", true),
                tcpNoDelay: config.GetBoolean("tcp-nodelay", true),
                backlog: config.GetInt("backlog", 4096),
                enforceIpFamily: RuntimeDetector.IsMono || config.GetBoolean("enforce-ip-family", false),
                receiveBufferSize: ToNullableInt(config.GetByteSize("receive-buffer-size") ?? 256000),
                sendBufferSize: ToNullableInt(config.GetByteSize("send-buffer-size") ?? 256000),
                writeBufferHighWaterMark: ToNullableInt(config.GetByteSize("write-buffer-high-water-mark")),
                writeBufferLowWaterMark: ToNullableInt(config.GetByteSize("write-buffer-low-water-mark")));
        }

        private static int? ToNullableInt(long? value) => value.HasValue ? (int?) value.Value : null;

        private static int ComputeWorkerPoolSize(Config config)
        {
            if (config == null) return ThreadPoolConfig.ScaledPoolSize(2, 1.0, 2);

            return ThreadPoolConfig.ScaledPoolSize(
                floor: config.GetInt("pool-size-min"),
                scalar: config.GetDouble("pool-size-factor"),
                ceiling: config.GetInt("pool-size-max"));
        }

        public readonly TransportMode TransportMode;
        public readonly bool EnableSsl;
        public readonly TimeSpan ConnectTimeout;
        public readonly string Host;
        public readonly int Port;
        public readonly int ServerSocketWorkerPoolSize;
        public readonly int ClientSocketWorkerPoolSize;
        public readonly int MaxFrameSize;
        public readonly SslSettings Ssl;
        public readonly bool DnsUseIpv6;
        public readonly bool TcpReuseAddress;
        public readonly bool TcpKeepAlive;
        public readonly bool TcpNoDelay;
        public readonly bool EnforceIpFamily;
        public readonly int Backlog;
        public readonly int? ReceiveBufferSize;
        public readonly int? SendBufferSize;
        public readonly int? WriteBufferHighWaterMark;
        public readonly int? WriteBufferLowWaterMark;

        public DotNettySettings(TransportMode transportMode, bool enableSsl, TimeSpan connectTimeout, string host, 
            int port, int serverSocketWorkerPoolSize, int clientSocketWorkerPoolSize, int maxFrameSize, SslSettings ssl, 
            bool dnsUseIpv6, bool tcpReuseAddress, bool tcpKeepAlive, bool tcpNoDelay, int backlog, bool enforceIpFamily, 
            int? receiveBufferSize, int? sendBufferSize, int? writeBufferHighWaterMark, int? writeBufferLowWaterMark)
        {
            if (maxFrameSize < 32000) throw new ArgumentException("maximum-frame-size must be at least 32000 bytes", nameof(maxFrameSize));

            TransportMode = transportMode;
            EnableSsl = enableSsl;
            ConnectTimeout = connectTimeout;
            Host = host;
            Port = port;
            ServerSocketWorkerPoolSize = serverSocketWorkerPoolSize;
            ClientSocketWorkerPoolSize = clientSocketWorkerPoolSize;
            MaxFrameSize = maxFrameSize;
            Ssl = ssl;
            DnsUseIpv6 = dnsUseIpv6;
            TcpReuseAddress = tcpReuseAddress;
            TcpKeepAlive = tcpKeepAlive;
            TcpNoDelay = tcpNoDelay;
            Backlog = backlog;
            EnforceIpFamily = enforceIpFamily;
            ReceiveBufferSize = receiveBufferSize;
            SendBufferSize = sendBufferSize;
            WriteBufferHighWaterMark = writeBufferHighWaterMark;
            WriteBufferLowWaterMark = writeBufferLowWaterMark;
        }
    }
    public enum TransportMode
    {
        Tcp,
        Udp
    }

    public sealed class SslSettings
    {
        public static readonly SslSettings Empty = new SslSettings(null, null);
        public static SslSettings Create(Config config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config), "DotNetty SSL HOCON config was not found (default path: `akka.remote.dot-netty.Ssl`)");

            return new SslSettings(
                certificatePath: config.GetString("certificate.path"),
                certificatePassword: config.GetString("certificate.password"));
        }

        public readonly X509Certificate2 Certificate;

        public SslSettings(string certificatePath, string certificatePassword)
        {
            Certificate = !string.IsNullOrEmpty(certificatePath) 
                ? new X509Certificate2(certificatePath, certificatePassword)
                : null;
        }
    }
}