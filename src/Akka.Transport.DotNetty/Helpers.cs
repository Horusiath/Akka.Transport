using System;
using System.Net;
using System.Net.Sockets;
using Akka.Actor;

namespace Akka.Transport.DotNetty
{
    public static class Helpers
    {
        public static Address MapSocketToAddress(IPEndPoint socketAddress, string schemeIdentifier, string systemName, string hostName)
        {
            return socketAddress == null
                ? null
                : new Address(schemeIdentifier, systemName, SafeMapHostName(hostName) ?? SafeMapIPv6(socketAddress.Address), socketAddress.Port);
        }

        private static string SafeMapHostName(string hostName)
        {
            IPAddress ip;
            return !string.IsNullOrEmpty(hostName) && IPAddress.TryParse(hostName, out ip) ? SafeMapIPv6(ip) : hostName;
        }

        private static string SafeMapIPv6(IPAddress ip) => ip.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + ip + "]" : ip.ToString();

        public static EndPoint ToEndpoint(Address address)
        {
            if (!address.Port.HasValue) throw new ArgumentNullException(nameof(address), $"Address port must not be null: {address}");

            IPAddress ip;
            return IPAddress.TryParse(address.Host, out ip)
                ? (EndPoint) new IPEndPoint(ip, address.Port.Value)
                : new DnsEndPoint(address.Host, address.Port.Value);
        }
    }
}