using Akka.Actor;
using Akka.Remote.Transport;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using Google.ProtocolBuffers;

namespace Akka.Transport.DotNetty
{
    public class TcpAssociationHandle : AssociationHandle
    {
        private readonly IChannel channel;

        public TcpAssociationHandle(Address localAddress, Address remoteAddress, IChannel channel) 
            : base(localAddress, remoteAddress)
        {
            this.channel = channel;
        }

        public override bool Write(ByteString payload)
        {
            if (channel.Open && channel.IsWritable)
            {
                var data = ToByteBuffer(payload);
                channel.WriteAndFlushAsync(data);
                return true;
            }
            return false;
        }

        private IByteBuffer ToByteBuffer(ByteString payload)
        {
            //TODO: optimize DotNetty byte buffer usage
            var data = payload.ToByteArray();
            var buffer = new UnpooledHeapByteBuffer(UnpooledByteBufferAllocator.Default, data, data.Length);
            return buffer;
        }

        public override void Disassociate()
        {
            channel.CloseAsync();
        }
    }
}