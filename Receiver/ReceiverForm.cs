using System;
using System.Drawing;
using System.Windows.Forms;

namespace ExtentDesktop.Receiver
{
    internal sealed class ReceiverForm : Form
    {
        private readonly TextBox _hostTextBox;
        private readonly TextBox _portTextBox;
        private readonly TextBox _passwordTextBox;
        private readonly Button _connectButton;
        private readonly Button _disconnectButton;
        private readonly Button _fullscreenButton;
        private readonly PictureBox _pictureBox;
        private readonly Label _statusLabel;

        private DisplayReceiverClient _client;
        private Bitmap _currentFrame;
        private Rectangle _normalBounds;
        private FormBorderStyle _normalBorderStyle;
        private bool _isFullscreen;

        public ReceiverForm()
        {
            Text = "ExtentDesktop Receiver";
            Width = 1360;
            Height = 820;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72
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

            topPanel.Controls.Add(hostLabel);
            topPanel.Controls.Add(_hostTextBox);
            topPanel.Controls.Add(portLabel);
            topPanel.Controls.Add(_portTextBox);
            topPanel.Controls.Add(passwordLabel);
            topPanel.Controls.Add(_passwordTextBox);
            topPanel.Controls.Add(_connectButton);
            topPanel.Controls.Add(_disconnectButton);
            topPanel.Controls.Add(_fullscreenButton);
            topPanel.Controls.Add(_statusLabel);

            _pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom
            };

            Controls.Add(_pictureBox);
            Controls.Add(topPanel);

            FormClosed += ReceiverForm_FormClosed;
            KeyDown += ReceiverForm_KeyDown;
        }

        private void ReceiverForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }

            if (_currentFrame != null)
            {
                _currentFrame.Dispose();
                _currentFrame = null;
            }
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

        private void UpdateFrame(Bitmap frame, int width, int height)
        {
            if (IsDisposed)
            {
                frame.Dispose();
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action<Bitmap, int, int>(UpdateFrame), frame, width, height);
                return;
            }

            var previous = _currentFrame;
            _currentFrame = frame;
            _pictureBox.Image = _currentFrame;
            Text = "ExtentDesktop Receiver - " + width + "x" + height;

            if (previous != null)
            {
                previous.Dispose();
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

        private void ToggleFullscreen()
        {
            if (!_isFullscreen)
            {
                _normalBounds = Bounds;
                _normalBorderStyle = FormBorderStyle;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                Bounds = Screen.FromControl(this).Bounds;
                _isFullscreen = true;
                _fullscreenButton.Text = "Exit Fullscreen";
                return;
            }

            FormBorderStyle = _normalBorderStyle;
            Bounds = _normalBounds;
            _isFullscreen = false;
            _fullscreenButton.Text = "Fullscreen";
        }
    }
}
