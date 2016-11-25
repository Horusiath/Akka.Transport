using System.Net;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Remote.Transport;
using DotNetty.Transport.Channels;

namespace Akka.Transport.DotNetty
{
    /// <summary>
    /// TCP handlers for inbound channels.
    /// </summary>
    internal sealed class TcpServerHandler : TcpHandler
    {
        private readonly Task<IAssociationEventListener> associationEventListener;

        public TcpServerHandler(TcpTransport transport, ILoggingAdapter log, Task<IAssociationEventListener> associationEventListener)
            : base(transport, log)
        {
            this.associationEventListener = associationEventListener;
            throw new System.NotImplementedException();
        }

        protected override void Activate(IChannel channel, IPEndPoint remoteAddress, object msg)
        {
            channel.Configuration.AutoRead = false;
            associationEventListener.ContinueWith(task =>
            {
                var listener = task.Result;
                var address = Helpers.MapSocketToAddress(remoteAddress, this.Transport.SchemeIdentifier, this.Transport.System.Name, null);

                AssociationHandle handle;
                Initialize(channel, remoteAddress, address, msg, out handle);
                listener.Notify(new InboundAssociation(handle));
            }, TaskContinuationOptions.NotOnRanToCompletion);
        }
    }
}