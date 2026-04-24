using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ExtentDesktop.Shared;

namespace ExtentDesktop.Receiver
{
    internal sealed class HostDiscoveryListener : IDisposable
    {
        private readonly Action<DiscoveredHostInfo> _hostCallback;

        private UdpClient _client;
        private Thread _thread;
        private volatile bool _running;

        public HostDiscoveryListener(Action<DiscoveredHostInfo> hostCallback)
        {
            _hostCallback = hostCallback;
        }

        public void Start()
        {
            if (_running)
            {
                return;
            }

            _client = new UdpClient(AddressFamily.InterNetwork);
            _client.ExclusiveAddressUse = false;
            _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _client.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryProtocol.BroadcastPort));
            _client.EnableBroadcast = true;
            _running = true;
            _thread = new Thread(ListenLoop);
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

        private void ListenLoop()
        {
            while (_running)
            {
                IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);

                try
                {
                    var data = _client.Receive(ref endpoint);
                    DiscoveredHostInfo host;
                    if (DiscoveryProtocol.TryParseAnnouncement(data, data.Length, endpoint.Address.ToString(), out host))
                    {
                        _hostCallback(host);
                    }
                }
                catch (SocketException)
                {
                    if (!_running)
                    {
                        return;
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch
                {
                }
            }
        }
    }
}
