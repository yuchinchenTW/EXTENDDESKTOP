using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using ExtentDesktop.Shared;

namespace ExtentDesktop.Receiver
{
    internal sealed class ReceiverForm : Form
    {
        private readonly Panel _topPanel;
        private readonly TextBox _hostTextBox;
        private readonly TextBox _portTextBox;
        private readonly TextBox _passwordTextBox;
        private readonly Button _connectButton;
        private readonly Button _disconnectButton;
        private readonly Button _fullscreenButton;
        private readonly ListView _hostsListView;
        private readonly HqPictureBox _pictureBox;
        private readonly Label _statusLabel;
        private readonly Label _infoLabel;
        private readonly Timer _discoveryRefreshTimer;
        private readonly Timer _fpsTimer;
        private int _fpsCounter;

        private DisplayReceiverClient _client;
        private HostDiscoveryListener _discoveryListener;
        private Bitmap _currentFrame;
        private Bitmap _pendingFrame;
        private int _pendingWidth;
        private int _pendingHeight;
        private bool _paintScheduled;
        private readonly object _frameSync = new object();
        private Rectangle _normalBounds;
        private FormBorderStyle _normalBorderStyle;
        private bool _isFullscreen;
        private readonly Dictionary<string, DiscoveredHostInfo> _discoveredHosts = new Dictionary<string, DiscoveredHostInfo>();

        public ReceiverForm()
        {
            Text = "ExtentDesktop Receiver";
            Width = 1360;
            Height = 820;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            _topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 92
            };

            var hostLabel = new Label
            {
                Left = 12,
                Top = 14,
                Width = 42,
                Text = "Host"
            };

            _hostTextBox = new TextBox
            {
                Left = 58,
                Top = 10,
                Width = 170,
                Text = "127.0.0.1"
            };

            var portLabel = new Label
            {
                Left = 242,
                Top = 14,
                Width = 38,
                Text = "Port"
            };

            _portTextBox = new TextBox
            {
                Left = 280,
                Top = 10,
                Width = 70,
                Text = "6201"
            };

            var passwordLabel = new Label
            {
                Left = 366,
                Top = 14,
                Width = 66,
                Text = "Password"
            };

            _passwordTextBox = new TextBox
            {
                Left = 436,
                Top = 10,
                Width = 170,
                UseSystemPasswordChar = true,
                Text = "changeme"
            };

            _connectButton = new Button
            {
                Left = 626,
                Top = 8,
                Width = 94,
                Text = "Connect"
            };
            _connectButton.Click += ConnectButton_Click;

            _disconnectButton = new Button
            {
                Left = 728,
                Top = 8,
                Width = 94,
                Text = "Disconnect",
                Enabled = false
            };
            _disconnectButton.Click += DisconnectButton_Click;

            _fullscreenButton = new Button
            {
                Left = 830,
                Top = 8,
                Width = 108,
                Text = "Fullscreen"
            };
            _fullscreenButton.Click += FullscreenButton_Click;

            _statusLabel = new Label
            {
                Left = 12,
                Top = 42,
                Width = 1180,
                Height = 18,
                Text = "Status: Not connected."
            };

            _infoLabel = new Label
            {
                Left = 12,
                Top = 64,
                Width = 1180,
                Height = 18,
                Text = "Hosts on the same LAN auto-appear on the right. Select one to auto-fill host and port."
            };

            _topPanel.Controls.Add(hostLabel);
            _topPanel.Controls.Add(_hostTextBox);
            _topPanel.Controls.Add(portLabel);
            _topPanel.Controls.Add(_portTextBox);
            _topPanel.Controls.Add(passwordLabel);
            _topPanel.Controls.Add(_passwordTextBox);
            _topPanel.Controls.Add(_connectButton);
            _topPanel.Controls.Add(_disconnectButton);
            _topPanel.Controls.Add(_fullscreenButton);
            _topPanel.Controls.Add(_statusLabel);
            _topPanel.Controls.Add(_infoLabel);

            _hostsListView = new ListView
            {
                Dock = DockStyle.Right,
                Width = 360,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false
            };
            _hostsListView.Columns.Add("Name", 110);
            _hostsListView.Columns.Add("IP", 105);
            _hostsListView.Columns.Add("Port", 45);
            _hostsListView.Columns.Add("Display", 80);
            _hostsListView.SelectedIndexChanged += HostsListView_SelectedIndexChanged;
            _hostsListView.DoubleClick += HostsListView_DoubleClick;

            _pictureBox = new HqPictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom
            };

            var contentPanel = new Panel
            {
                Dock = DockStyle.Fill
            };
            contentPanel.Controls.Add(_pictureBox);
            contentPanel.Controls.Add(_hostsListView);

            Controls.Add(contentPanel);
            Controls.Add(_topPanel);

            FormClosed += ReceiverForm_FormClosed;
            KeyDown += ReceiverForm_KeyDown;

            _discoveryListener = new HostDiscoveryListener(OnHostDiscovered);
            _discoveryListener.Start();

            _discoveryRefreshTimer = new Timer();
            _discoveryRefreshTimer.Interval = 1000;
            _discoveryRefreshTimer.Tick += DiscoveryRefreshTimer_Tick;
            _discoveryRefreshTimer.Start();

            _fpsTimer = new Timer();
            _fpsTimer.Interval = 1000;
            _fpsTimer.Tick += FpsTimer_Tick;
            _fpsTimer.Start();
        }

        private void ReceiverForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (_discoveryRefreshTimer != null)
            {
                _discoveryRefreshTimer.Stop();
                _discoveryRefreshTimer.Dispose();
            }

            if (_fpsTimer != null)
            {
                _fpsTimer.Stop();
                _fpsTimer.Dispose();
            }

            if (_discoveryListener != null)
            {
                _discoveryListener.Dispose();
                _discoveryListener = null;
            }

            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }

            if (_currentFrame != null)
            {
                DisposeFrame(_currentFrame);
                _currentFrame = null;
            }

            Bitmap leftover;
            lock (_frameSync)
            {
                leftover = _pendingFrame;
                _pendingFrame = null;
            }

            if (leftover != null)
            {
                DisposeFrame(leftover);
            }
        }

        private void DiscoveryRefreshTimer_Tick(object sender, EventArgs e)
        {
            RefreshDiscoveredHosts();
        }

        private void FpsTimer_Tick(object sender, EventArgs e)
        {
            var count = System.Threading.Interlocked.Exchange(ref _fpsCounter, 0);
            _pictureBox.Fps = count;
            _pictureBox.Invalidate();
        }

        private void ConnectButton_Click(object sender, EventArgs e)
        {
            int port;
            if (!int.TryParse(_portTextBox.Text, out port) || port < 1 || port > 65535)
            {
                MessageBox.Show(this, "Enter a valid TCP port.", "Invalid Port", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                _client = new DisplayReceiverClient(UpdateStatus, UpdateFrame);
                _client.Connect(_hostTextBox.Text.Trim(), port, _passwordTextBox.Text);
                _connectButton.Enabled = false;
                _disconnectButton.Enabled = true;
                _hostTextBox.Enabled = false;
                _portTextBox.Enabled = false;
                _passwordTextBox.Enabled = false;
            }
            catch (Exception ex)
            {
                if (_client != null)
                {
                    _client.Dispose();
                    _client = null;
                }

                MessageBox.Show(this, ex.Message, "Connection Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateStatus("Connection failed.");
            }
        }

        private void DisconnectButton_Click(object sender, EventArgs e)
        {
            Disconnect();
        }

        private void HostsListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_hostsListView.SelectedItems.Count == 0)
            {
                return;
            }

            var info = _hostsListView.SelectedItems[0].Tag as DiscoveredHostInfo;
            if (info == null)
            {
                return;
            }

            _hostTextBox.Text = info.HostAddress;
            _portTextBox.Text = info.HostPort.ToString();
        }

        private void HostsListView_DoubleClick(object sender, EventArgs e)
        {
            if (_hostsListView.SelectedItems.Count == 0 || !_connectButton.Enabled)
            {
                return;
            }

            ConnectButton_Click(sender, e);
        }

        private void FullscreenButton_Click(object sender, EventArgs e)
        {
            ToggleFullscreen();
        }

        private void ReceiverForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F11 || e.KeyCode == Keys.Escape && _isFullscreen)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
        }

        private void UpdateStatus(string text)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateStatus), text);
                return;
            }

            _statusLabel.Text = "Status: " + text;
            if (text == "Disconnected.")
            {
                ResetConnectionUiOnly();
            }
        }

        private void OnHostDiscovered(DiscoveredHostInfo host)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action<DiscoveredHostInfo>(OnHostDiscovered), host);
                return;
            }

            _discoveredHosts[BuildHostKey(host)] = host;
            RefreshDiscoveredHosts();
        }

        private void UpdateFrame(Bitmap frame, int width, int height)
        {
            if (IsDisposed)
            {
                DisposeFrame(frame);
                return;
            }

            Bitmap droppedFrame = null;
            bool needSchedule = false;

            lock (_frameSync)
            {
                if (_pendingFrame != null)
                {
                    droppedFrame = _pendingFrame;
                }

                _pendingFrame = frame;
                _pendingWidth = width;
                _pendingHeight = height;

                if (!_paintScheduled)
                {
                    _paintScheduled = true;
                    needSchedule = true;
                }
            }

            if (droppedFrame != null)
            {
                DisposeFrame(droppedFrame);
            }

            if (needSchedule)
            {
                BeginInvoke(new Action(ApplyPendingFrame));
            }
        }

        private void ApplyPendingFrame()
        {
            Bitmap frame;
            int width;
            int height;

            lock (_frameSync)
            {
                frame = _pendingFrame;
                width = _pendingWidth;
                height = _pendingHeight;
                _pendingFrame = null;
                _paintScheduled = false;
            }

            if (frame == null || IsDisposed)
            {
                if (frame != null)
                {
                    DisposeFrame(frame);
                }
                return;
            }

            var previous = _currentFrame;
            _currentFrame = frame;
            _pictureBox.Image = _currentFrame;
            Text = "ExtentDesktop Receiver - " + width + "x" + height;
            System.Threading.Interlocked.Increment(ref _fpsCounter);

            if (previous != null)
            {
                DisposeFrame(previous);
            }
        }

        private static void DisposeFrame(Bitmap frame)
        {
            if (frame == null)
            {
                return;
            }

            var pool = frame.Tag as FrameBitmapPool;
            if (pool != null)
            {
                frame.Tag = null;
                pool.Return(frame);
                return;
            }

            var stream = frame.Tag as IDisposable;
            frame.Dispose();
            if (stream != null)
            {
                stream.Dispose();
            }
        }

        private void Disconnect()
        {
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }

            ResetConnectionUiOnly();
            UpdateStatus("Disconnected.");
        }

        private void ResetConnectionUiOnly()
        {
            _connectButton.Enabled = true;
            _disconnectButton.Enabled = false;
            _hostTextBox.Enabled = true;
            _portTextBox.Enabled = true;
            _passwordTextBox.Enabled = true;
        }

        private void RefreshDiscoveredHosts()
        {
            var now = DateTime.UtcNow;
            var expiredKeys = _discoveredHosts
                .Where(pair => (now - pair.Value.LastSeenUtc).TotalMilliseconds > DiscoveryProtocol.HostTimeoutMs)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _discoveredHosts.Remove(key);
            }

            var selectedKey = _hostsListView.SelectedItems.Count > 0 ? _hostsListView.SelectedItems[0].Name : null;

            _hostsListView.BeginUpdate();
            _hostsListView.Items.Clear();

            foreach (var host in _discoveredHosts.Values
                .OrderBy(host => host.MachineName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(host => host.HostAddress, StringComparer.Ordinal))
            {
                var item = new ListViewItem(string.IsNullOrWhiteSpace(host.MachineName) ? host.HostAddress : host.MachineName);
                item.Name = BuildHostKey(host);
                item.Tag = host;
                item.SubItems.Add(host.HostAddress);
                item.SubItems.Add(host.HostPort.ToString());
                item.SubItems.Add(string.IsNullOrWhiteSpace(host.DisplayLabel) ? "-" : host.DisplayLabel);
                _hostsListView.Items.Add(item);

                if (!string.IsNullOrEmpty(selectedKey) && string.Equals(selectedKey, item.Name, StringComparison.Ordinal))
                {
                    item.Selected = true;
                }
            }

            _hostsListView.EndUpdate();
        }

        private static string BuildHostKey(DiscoveredHostInfo host)
        {
            return host.HostAddress + ":" + host.HostPort;
        }

        private void ToggleFullscreen()
        {
            if (!_isFullscreen)
            {
                _normalBounds = Bounds;
                _normalBorderStyle = FormBorderStyle;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                Bounds = Screen.FromControl(this).Bounds;
                _topPanel.Visible = false;
                _hostsListView.Visible = false;
                _isFullscreen = true;
                _fullscreenButton.Text = "Exit Fullscreen";
                return;
            }

            FormBorderStyle = _normalBorderStyle;
            Bounds = _normalBounds;
            _topPanel.Visible = true;
            _hostsListView.Visible = true;
            _isFullscreen = false;
            _fullscreenButton.Text = "Fullscreen";
        }

        private sealed class HqPictureBox : PictureBox
        {
            private static readonly Font OverlayFont = new Font("Consolas", 11, FontStyle.Bold);
            private static readonly Brush OverlayBackBrush = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
            private static readonly Brush OverlayForeBrush = new SolidBrush(Color.FromArgb(255, 80, 255, 120));

            public int Fps { get; set; }

            public HqPictureBox()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            }

            protected override void OnPaint(PaintEventArgs pe)
            {
                pe.Graphics.InterpolationMode = InterpolationMode.Bilinear;
                pe.Graphics.PixelOffsetMode = PixelOffsetMode.None;
                pe.Graphics.SmoothingMode = SmoothingMode.None;
                pe.Graphics.CompositingQuality = CompositingQuality.HighSpeed;
                pe.Graphics.CompositingMode = CompositingMode.SourceCopy;
                base.OnPaint(pe);

                var fps = Fps;
                if (fps > 0)
                {
                    pe.Graphics.CompositingMode = CompositingMode.SourceOver;
                    var text = fps + " fps";
                    var size = pe.Graphics.MeasureString(text, OverlayFont);
                    var rect = new RectangleF(Width - size.Width - 12, 8, size.Width + 8, size.Height + 4);
                    pe.Graphics.FillRectangle(OverlayBackBrush, rect);
                    pe.Graphics.DrawString(text, OverlayFont, OverlayForeBrush, rect.X + 4, rect.Y + 2);
                }
            }
        }
    }
}
