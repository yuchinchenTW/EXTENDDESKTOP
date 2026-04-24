using System;
using System.Net;
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
            var endpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryProtocol.BroadcastPort);

            while (_running)
            {
                try
                {
                    var payload = DiscoveryProtocol.CreateAnnouncement(Environment.MachineName, _port, GetDisplayLabel());
                    _client.Send(payload, payload.Length, endpoint);
                }
                catch
                {
                }

                Thread.Sleep(DiscoveryProtocol.BroadcastIntervalMs);
            }
        }

        private string GetDisplayLabel()
        {
            return _displayLabelProvider != null ? _displayLabelProvider() : "Selected Display";
        }
    }
}
