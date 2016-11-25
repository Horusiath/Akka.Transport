using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Remote.Transport;
using DotNetty.Transport.Channels;

namespace Akka.Transport.DotNetty
{
    /// <summary>
    /// TCP handler for outbound connections.
    /// </summary>
    internal sealed class TcpClientHandler : TcpHandler
    {
        private readonly Address remoteAddress;
        private readonly TaskCompletionSource<AssociationHandle> statusPromise;

        public Task<AssociationHandle> StatusTask => this.statusPromise.Task;

        public TcpClientHandler(TcpTransport transport, ILoggingAdapter log, Address remoteAddress)
            : base(transport, log)
        {
            this.remoteAddress = remoteAddress;
            this.statusPromise = new TaskCompletionSource<AssociationHandle>();
        }

        protected override void Activate(IChannel channel, IPEndPoint socketAddress, object msg)
        {
            AssociationHandle handle;
            Initialize(channel, socketAddress, remoteAddress, msg, out handle);
            this.statusPromise.SetResult(handle);
        }
    }
}