using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using ExtentDesktop.Shared;

namespace ExtentDesktop.Host
{
    internal sealed class HostDiscoveryBroadcaster : IDisposable
    {
        private readonly Func<string> _displayLabelProvider;

        private UdpClient _client;
        private Thread _thread;
        private volatile bool _running;
        private int _port;

        public HostDiscoveryBroadcaster(Func<string> displayLabelProvider)
        {
            _displayLabelProvider = displayLabelProvider;
        }

        public void Start(int port)
        {
            if (_running)
            {
                return;
            }

            _port = port;
            _client = new UdpClient();
            _client.EnableBroadcast = true;
            _running = true;
            _thread = new Thread(BroadcastLoop);
            _thread.IsBackground = true;
            _thread.Start();
        }

        public void Dispose()
        {
            _running = false;

            if (_client != null)
            {
                try
                {
                    _client.Close();
                }
                catch
                {
                }
            }

            if (_thread != null && _thread != Thread.CurrentThread)
            {
                _thread.Join(500);
            }
        }

        private void BroadcastLoop()
        {
            while (_running)
            {
                var payload = DiscoveryProtocol.CreateAnnouncement(Environment.MachineName, _port, GetDisplayLabel());

                foreach (var endpoint in GetBroadcastEndpoints())
                {
                    try
                    {
                        _client.Send(payload, payload.Length, endpoint);
                    }
                    catch
                    {
                    }
                }

                Thread.Sleep(DiscoveryProtocol.BroadcastIntervalMs);
            }
        }

        private static IEnumerable<IPEndPoint> GetBroadcastEndpoints()
        {
            var endpoints = new List<IPEndPoint>();

            try
            {
                var adapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                                      && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                foreach (var adapter in adapters)
                {
                    foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null)
                        {
                            continue;
                        }

                        endpoints.Add(new IPEndPoint(GetBroadcastAddress(unicast.Address, unicast.IPv4Mask), DiscoveryProtocol.BroadcastPort));
                    }
                }
            }
            catch
            {
            }

            endpoints.Add(new IPEndPoint(IPAddress.Broadcast, DiscoveryProtocol.BroadcastPort));

            return endpoints
                .GroupBy(endpoint => endpoint.Address.ToString() + ":" + endpoint.Port)
                .Select(group => group.First());
        }

        private static IPAddress GetBroadcastAddress(IPAddress address, IPAddress subnetMask)
        {
            var addressBytes = address.GetAddressBytes();
            var maskBytes = subnetMask.GetAddressBytes();
            var broadcastBytes = new byte[addressBytes.Length];

            for (var i = 0; i < addressBytes.Length; i++)
            {
                broadcastBytes[i] = (byte)(addressBytes[i] | (maskBytes[i] ^ 255));
            }

            return new IPAddress(broadcastBytes);
        }

        private string GetDisplayLabel()
        {
            return _displayLabelProvider != null ? _displayLabelProvider() : "Selected Display";
        }
    }
}
