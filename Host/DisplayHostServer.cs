using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ExtentDesktop.Shared;

namespace ExtentDesktop.Host
{
    internal sealed class DisplayHostServer : IDisposable
    {
        private readonly Action<string> _statusCallback;
        private readonly Action<string> _clientCallback;

        private TcpListener _listener;
        private Thread _acceptThread;
        private CancellationTokenSource _sessionTokenSource;
        private HostDiscoveryBroadcaster _discoveryBroadcaster;
        private volatile bool _running;
        private string _password;
        private int _port;
        private Func<Rectangle> _captureBoundsProvider;
        private Func<string> _captureLabelProvider;

        public DisplayHostServer(Action<string> statusCallback, Action<string> clientCallback)
        {
            _statusCallback = statusCallback;
            _clientCallback = clientCallback;
        }

        public void Start(int port, string password, Func<Rectangle> captureBoundsProvider, Func<string> captureLabelProvider)
        {
            if (_running)
            {
                return;
            }

            _password = password ?? string.Empty;
            _port = port;
            _captureBoundsProvider = captureBoundsProvider;
            _captureLabelProvider = captureLabelProvider;
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _discoveryBroadcaster = new HostDiscoveryBroadcaster(GetCaptureLabel);
            _discoveryBroadcaster.Start(port);
            _running = true;
            _acceptThread = new Thread(AcceptLoop);
            _acceptThread.IsBackground = true;
            _acceptThread.Start();
            _statusCallback("Listening on port " + port + " for " + GetCaptureLabel() + ".");
        }

        public void Dispose()
        {
            _running = false;

            if (_sessionTokenSource != null)
            {
                _sessionTokenSource.Cancel();
            }

            if (_discoveryBroadcaster != null)
            {
                _discoveryBroadcaster.Dispose();
                _discoveryBroadcaster = null;
            }

            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                }
                catch
                {
                }
            }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client = null;

                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    if (!_running)
                    {
                        return;
                    }

                    _statusCallback("Socket error while listening. Retrying...");
                    Thread.Sleep(500);
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                try
                {
                    HandleClient(client);
                }
                catch (Exception ex)
                {
                    _statusCallback("Session ended: " + ex.Message);
                }
                finally
                {
                    if (client != null)
                    {
                        try
                        {
                            client.Close();
                        }
                        catch
                        {
                        }
                    }

                    if (_sessionTokenSource != null)
                    {
                        _sessionTokenSource.Dispose();
                        _sessionTokenSource = null;
                    }

                    _clientCallback("No receiver connected.");

                    if (_running)
                    {
                        _statusCallback("Listening on port " + _port + " for " + GetCaptureLabel() + ".");
                    }
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            client.NoDelay = true;
            _clientCallback("Receiver connected from " + client.Client.RemoteEndPoint + ".");

            using (var stream = client.GetStream())
            {
                var writeSync = new object();
                var auth = Protocol.ReceiveMessage(stream);
                if (auth.Type != MessageType.AuthRequest)
                {
                    throw new InvalidDataException("Expected auth request.");
                }

                string providedPassword;
                using (var reader = Protocol.CreateReader(auth.Payload))
                {
                    providedPassword = reader.ReadString();
                }

                var isPasswordValid = string.Equals(providedPassword, _password, StringComparison.Ordinal);
                Protocol.SendMessage(stream, writeSync, MessageType.AuthResponse, delegate(BinaryWriter writer)
                {
                    writer.Write(isPasswordValid);
                    writer.Write(isPasswordValid ? "Connected." : "Password mismatch.");
                });

                if (!isPasswordValid)
                {
                    _statusCallback("Rejected receiver from " + client.Client.RemoteEndPoint + ".");
                    return;
                }

                _statusCallback("Streaming " + GetCaptureLabel() + " to " + client.Client.RemoteEndPoint + ".");
                _sessionTokenSource = new CancellationTokenSource();
                ScreenCaptureStreamer.StreamFrames(stream, writeSync, _sessionTokenSource.Token, 30, _captureBoundsProvider);
            }
        }

        private string GetCaptureLabel()
        {
            return _captureLabelProvider != null ? _captureLabelProvider() : "selected display";
        }
    }
}
