using System;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Remote.Transport;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using Google.ProtocolBuffers;

namespace Akka.Transport.DotNetty
{
    internal abstract class TcpHandler : ChannelHandlerAdapter
    {
        public const TaskContinuationOptions ReaderHandlerSourceContinuationOptions =
            TaskContinuationOptions.ExecuteSynchronously | 
            TaskContinuationOptions.NotOnCanceled |
            TaskContinuationOptions.NotOnFaulted;

        public static readonly Disassociated Disassociated = new Disassociated(DisassociateInfo.Unknown);

        protected readonly TcpTransport Transport;
        protected readonly ILoggingAdapter Log;
        private IHandleEventListener listener;

        protected TcpHandler(TcpTransport transport, ILoggingAdapter log)
        {
            this.Transport = transport;
            this.Log = log;
        }

        public sealed override void ChannelActive(IChannelHandlerContext context)
        {
            Activate(context.Channel, (IPEndPoint) context.Channel.RemoteAddress, null);
            base.ChannelActive(context);
            if (!Transport.TryAddChannel(context.Channel))
            {
                Log.Warning(
                    "Unable to ADD channel [{0}->{1}](Id={2}) to connection group. May not shut down cleanly.",
                    context.Channel.LocalAddress, context.Channel.RemoteAddress, context.Channel.Id);
            }
        }

        public sealed override void ChannelInactive(IChannelHandlerContext context)
        {
            base.ChannelInactive(context);

            if (!Transport.TryRemoveChannel(context.Channel))
            {
                Log.Warning(
                    "Unable to REMOVE channel [{0}->{1}](Id={2}) from connection group. May not shut down cleanly.",
                    context.Channel.LocalAddress, context.Channel.RemoteAddress, context.Channel.Id);
            }

            NotifyListener(Disassociated);
        }

        public sealed override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            base.ExceptionCaught(context, exception);
            Log.Error(exception, 
                "Error caught channel [{0}->{1}](Id={2})", 
                context.Channel.LocalAddress, context.Channel.RemoteAddress, context.Channel.Id);
            NotifyListener(Disassociated);
            context.CloseAsync();
        }

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            base.ChannelRead(context, message);
            var buffer = (IByteBuffer) message;
            if (buffer.ReadableBytes > 0)
            {
                var byteString = ByteString.CopyFrom(buffer.Array, buffer.ArrayOffset + buffer.ReaderIndex, buffer.ReadableBytes);
                NotifyListener(new InboundPayload(byteString));
            }

            buffer.Release();
        }

        protected virtual void Initialize(IChannel channel, IPEndPoint remoteSocketAddress, Address remoteAddress, object msg, out AssociationHandle handle)
        {
            var localAddress = Helpers.MapSocketToAddress(channel.LocalAddress as IPEndPoint,
                this.Transport.SchemeIdentifier, 
                this.Transport.System.Name, 
                this.Transport.Settings.Host);

            if (localAddress != null)
            {
                handle = new TcpAssociationHandle(localAddress, remoteAddress, channel);
                handle.ReadHandlerSource.Task.ContinueWith(task =>
                {
                    this.listener = task.Result;
                    channel.Configuration.AutoRead = true;
                }, ReaderHandlerSourceContinuationOptions);
            }
            else
            {
                handle = null;
                channel.CloseAsync();
            }
        }

        protected void NotifyListener(IHandleEvent e) => this.listener?.Notify(e);

        protected abstract void Activate(IChannel channel, IPEndPoint remoteAddress, object msg);
    }
}