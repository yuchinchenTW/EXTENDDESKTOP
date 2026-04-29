using System;
using System.Drawing;
using System.Windows.Forms;

namespace ExtentDesktop.Host
{
    internal sealed class HostForm : Form
    {
        private readonly TextBox _portTextBox;
        private readonly TextBox _passwordTextBox;
        private readonly ComboBox _displayComboBox;
        private readonly Button _refreshButton;
        private readonly Button _startButton;
        private readonly Button _stopButton;
        private readonly Label _statusLabel;
        private readonly Label _clientLabel;
        private readonly TextBox _notesTextBox;

        private DisplayHostServer _server;
        private WebStreamHost _webServer;

        public HostForm()
        {
            Text = "ExtentDesktop Host";
            Width = 620;
            Height = 470;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var introLabel = new Label
            {
                Left = 20,
                Top = 18,
                Width = 560,
                Height = 42,
                Text = "Streams one selected desktop surface to a laptop receiver. To make the laptop act like a true third monitor, the desktop PC must already have a virtual display driver installed."
            };

            var portLabel = new Label
            {
                Left = 20,
                Top = 76,
                Width = 60,
                Text = "Port"
            };

            _portTextBox = new TextBox
            {
                Left = 82,
                Top = 72,
                Width = 110,
                Text = "6201"
            };

            var passwordLabel = new Label
            {
                Left = 212,
                Top = 76,
                Width = 70,
                Text = "Password"
            };

            _passwordTextBox = new TextBox
            {
                Left = 286,
                Top = 72,
                Width = 150,
                UseSystemPasswordChar = true,
                Text = "changeme"
            };

            var displayLabel = new Label
            {
                Left = 20,
                Top = 114,
                Width = 60,
                Text = "Display"
            };

            _displayComboBox = new ComboBox
            {
                Left = 82,
                Top = 110,
                Width = 354,
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            _refreshButton = new Button
            {
                Left = 448,
                Top = 108,
                Width = 60,
                Text = "Refresh"
            };
            _refreshButton.Click += RefreshButton_Click;

            _startButton = new Button
            {
                Left = 448,
                Top = 70,
                Width = 120,
                Text = "Start Host"
            };
            _startButton.Click += StartButton_Click;

            _stopButton = new Button
            {
                Left = 448,
                Top = 144,
                Width = 120,
                Text = "Stop",
                Enabled = false
            };
            _stopButton.Click += StopButton_Click;

            _statusLabel = new Label
            {
                Left = 20,
                Top = 156,
                Width = 410,
                Height = 20,
                Text = "Status: Idle."
            };

            _clientLabel = new Label
            {
                Left = 20,
                Top = 182,
                Width = 410,
                Height = 20,
                Text = "Receiver: No receiver connected."
            };

            _notesTextBox = new TextBox
            {
                Left = 20,
                Top = 214,
                Width = 548,
                Height = 196,
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical
            };

            Controls.Add(introLabel);
            Controls.Add(portLabel);
            Controls.Add(_portTextBox);
            Controls.Add(passwordLabel);
            Controls.Add(_passwordTextBox);
            Controls.Add(displayLabel);
            Controls.Add(_displayComboBox);
            Controls.Add(_refreshButton);
            Controls.Add(_startButton);
            Controls.Add(_stopButton);
            Controls.Add(_statusLabel);
            Controls.Add(_clientLabel);
            Controls.Add(_notesTextBox);

            Load += HostForm_Load;
            FormClosed += HostForm_FormClosed;
        }

        private void HostForm_Load(object sender, EventArgs e)
        {
            RefreshDisplays();
        }

        private void HostForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (_server != null)
            {
                _server.Dispose();
                _server = null;
            }
            if (_webServer != null)
            {
                _webServer.Dispose();
                _webServer = null;
            }
        }

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            RefreshDisplays();
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            int port;
            if (!int.TryParse(_portTextBox.Text, out port) || port < 1 || port > 65535)
            {
                MessageBox.Show(this, "Enter a valid TCP port.", "Invalid Port", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_passwordTextBox.Text))
            {
                MessageBox.Show(this, "Set a non-empty password.", "Invalid Password", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                _server = new DisplayHostServer(UpdateStatus, UpdateClient);
                _server.Start(port, _passwordTextBox.Text, GetSelectedCaptureBounds, GetSelectedDisplayLabel);

                try
                {
                    var indexHtml = LoadIndexHtml();
                    if (indexHtml != null)
                    {
                        _webServer = new WebStreamHost(UpdateStatus, UpdateClient);
                        _webServer.Start(port + 1, indexHtml, GetSelectedCaptureBounds, GetSelectedDisplayLabel);
                    }
                }
                catch (Exception webEx)
                {
                    UpdateStatus("Web stream not available: " + webEx.Message);
                }

                _startButton.Enabled = false;
                _stopButton.Enabled = true;
                _portTextBox.Enabled = false;
                _passwordTextBox.Enabled = false;
                _displayComboBox.Enabled = false;
                _refreshButton.Enabled = false;
                UpdateNotes();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Failed to Start Host", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            StopServer();
        }

        private void StopServer()
        {
            if (_server != null)
            {
                _server.Dispose();
                _server = null;
            }
            if (_webServer != null)
            {
                _webServer.Dispose();
                _webServer = null;
            }

            _startButton.Enabled = true;
            _stopButton.Enabled = false;
            _portTextBox.Enabled = true;
            _passwordTextBox.Enabled = true;
            _displayComboBox.Enabled = true;
            _refreshButton.Enabled = true;
            _statusLabel.Text = "Status: Stopped.";
            _clientLabel.Text = "Receiver: No receiver connected.";
        }

        private static byte[] LoadIndexHtml()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                var path = System.IO.Path.Combine(dir, "web", "index.html");
                if (System.IO.File.Exists(path))
                {
                    return System.IO.File.ReadAllBytes(path);
                }
            }
            catch
            {
            }
            return null;
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
        }

        private void UpdateClient(string text)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateClient), text);
                return;
            }

            _clientLabel.Text = "Receiver: " + text;
        }

        private void RefreshDisplays()
        {
            var selectedIndex = _displayComboBox.SelectedIndex;
            _displayComboBox.Items.Clear();
            _displayComboBox.Items.Add(new DisplayChoice("All Displays (combined desktop)", SystemInformation.VirtualScreen));

            var screens = Screen.AllScreens;
            for (var i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                var label = "Screen " + (i + 1) + "  " + screen.DeviceName + "  " + screen.Bounds.Width + "x" + screen.Bounds.Height + " @ " + screen.Bounds.Left + "," + screen.Bounds.Top;
                _displayComboBox.Items.Add(new DisplayChoice(label, screen.Bounds));
            }

            if (_displayComboBox.Items.Count > 0)
            {
                _displayComboBox.SelectedIndex = selectedIndex >= 0 && selectedIndex < _displayComboBox.Items.Count ? selectedIndex : 0;
            }

            UpdateNotes();
        }

        private Rectangle GetSelectedCaptureBounds()
        {
            var selected = _displayComboBox.SelectedItem as DisplayChoice;
            return selected != null ? selected.Bounds : SystemInformation.VirtualScreen;
        }

        private string GetSelectedDisplayLabel()
        {
            var selected = _displayComboBox.SelectedItem as DisplayChoice;
            return selected != null ? selected.Label : "All Displays";
        }

        private void UpdateNotes()
        {
            var selected = _displayComboBox.SelectedItem as DisplayChoice;
            var label = selected != null ? selected.Label : "All Displays";

            _notesTextBox.Text =
                "Selected source:\r\n" +
                label + "\r\n\r\n" +
                "How to use this project:\r\n" +
                "1. Run this Host on the desktop PC.\r\n" +
                "2. Run the Receiver app on the laptop and connect to this PC.\r\n" +
                "3. If you only choose an existing physical screen, the laptop will mirror that screen.\r\n" +
                "4. If you install a virtual display driver on the desktop PC and it appears in this list, selecting that virtual screen gives you the missing 'third monitor' workflow.\r\n\r\n" +
                "Important limitation:\r\n" +
                "Windows cannot create a brand-new extended monitor on the laptop through user-space streaming alone. The desktop PC needs a virtual or indirect display driver first.";
        }

        private sealed class DisplayChoice
        {
            public DisplayChoice(string label, Rectangle bounds)
            {
                Label = label;
                Bounds = bounds;
            }

            public string Label { get; private set; }
            public Rectangle Bounds { get; private set; }

            public override string ToString()
            {
                return Label;
            }
        }
    }
}
