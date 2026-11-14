using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

class MainForm : Form
{
    private readonly ToggleSwitch _controllerToggle = new() { Checked = true };
    private readonly ToggleSwitch _triggerVibrationToggle = new() { Enabled = false };
    private readonly Label _statusLabel = new();
    private readonly Label _statusIndicator = new();
    private CancellationTokenSource? _activationCancellation;
    private Task? _activationTask;
    private bool _closing;

    public MainForm()
    {
        Text = "DualSense Companion";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(960, 640);
        BackColor = Color.FromArgb(11, 16, 29);
        Font = new Font("Segoe UI", 10F);
        BackgroundImage = Image.FromFile(Path.Combine(AppContext.BaseDirectory, "img", "background.jpg"));
        BackgroundImageLayout = ImageLayout.Stretch;

        var logo = new PictureBox
        {
            BackColor = Color.Transparent,
            Image = Image.FromFile(Path.Combine(AppContext.BaseDirectory, "img", "logo.jpg")),
            SizeMode = PictureBoxSizeMode.Zoom,
            Bounds = new Rectangle(260, 28, 440, 130)
        };
        Controls.Add(logo);

        var card = new Panel
        {
            BackColor = Color.FromArgb(238, 15, 23, 40),
            Bounds = new Rectangle(130, 190, 700, 390)
        };
        Controls.Add(card);

        var heading = new Label
        {
            AutoSize = true,
            Text = "Controller setup",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 18F),
            Location = new Point(32, 24)
        };
        card.Controls.Add(heading);

        var description = new Label
        {
            AutoSize = true,
            Text = "Manage your PlayStation-to-Xbox controller bridge.",
            ForeColor = Color.FromArgb(170, 184, 205),
            Location = new Point(34, 64)
        };
        card.Controls.Add(description);

        AddSettingRow(
            card,
            "Controller emulation",
            "Hide the PlayStation controller and emulate an Xbox 360 controller.",
            115,
            _controllerToggle);

        AddSettingRow(
            card,
            "Adaptive-trigger vibration",
            "Mirror controller rumble into DualSense triggers (DualSense only).",
            205,
            _triggerVibrationToggle);

        var divider = new Panel
        {
            BackColor = Color.FromArgb(54, 68, 91),
            Bounds = new Rectangle(32, 300, 636, 1)
        };
        card.Controls.Add(divider);

        _statusIndicator = new Label
        {
            AutoSize = true,
            Text = "●",
            ForeColor = Color.FromArgb(244, 181, 75),
            Font = new Font("Segoe UI", 12F),
            Location = new Point(32, 331)
        };
        card.Controls.Add(_statusIndicator);

        _statusLabel = new Label
        {
            AutoSize = true,
            Text = "Ready to start",
            ForeColor = Color.FromArgb(205, 215, 230),
            Location = new Point(55, 334)
        };
        card.Controls.Add(_statusLabel);

        _controllerToggle.CheckedChanged += ControllerToggle_CheckedChanged;
        _triggerVibrationToggle.CheckedChanged += (_, _) =>
            ControllerManager.SetAdaptiveTriggerVibrationEnabled(_triggerVibrationToggle.Checked);
        Shown += (_, _) => StartController();
    }

    private static void AddSettingRow(
        Control parent,
        string title,
        string subtitle,
        int top,
        ToggleSwitch toggle)
    {
        var titleLabel = new Label
        {
            AutoSize = true,
            Text = title,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11F),
            Location = new Point(34, top)
        };
        parent.Controls.Add(titleLabel);

        var subtitleLabel = new Label
        {
            AutoSize = true,
            Text = subtitle,
            ForeColor = Color.FromArgb(157, 172, 194),
            Location = new Point(34, top + 29)
        };
        parent.Controls.Add(subtitleLabel);

        toggle.Bounds = new Rectangle(595, top + 5, 48, 28);
        parent.Controls.Add(toggle);
    }

    private void ControllerToggle_CheckedChanged(object? sender, EventArgs e)
    {
        if (_controllerToggle.Checked)
        {
            StartController();
        }
        else
        {
            _ = StopControllerAsync();
        }
    }

    private void StartController()
    {
        _triggerVibrationToggle.Checked = false;
        _triggerVibrationToggle.Enabled = false;
        _activationCancellation = new CancellationTokenSource();
        var cancellationToken = _activationCancellation.Token;
        SetStatus("Waiting for a PlayStation controller…", false);

        _activationTask = Task.Run(() =>
        {
            try
            {
                XboxEmulator.Initialize();
                cancellationToken.ThrowIfCancellationRequested();
                CloakManager.HideController(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                ControllerManager.InitializeVibration();
                while (!cancellationToken.IsCancellationRequested)
                {
                    ControllerManager.StartListening(cancellationToken, SetControllerType);

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        SetStatus("Controller disconnected · waiting to reconnect", false);
                        cancellationToken.WaitHandle.WaitOne(1000);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unable to start controller emulation: {ex}");
                CrashReporter.Report("Failed to start controller emulation", ex);
                SetStatus($"Unable to start: {ex.Message}", false);
            }
        });
    }

    private async Task StopControllerAsync()
    {
        _controllerToggle.Enabled = false;
        _triggerVibrationToggle.Checked = false;
        _triggerVibrationToggle.Enabled = false;
        ControllerManager.SetAdaptiveTriggerVibrationEnabled(false);
        _activationCancellation?.Cancel();
        ControllerManager.StopListening();

        if (_activationTask != null)
        {
            await _activationTask;
        }

        if (_closing)
        {
            return;
        }

        CleanupController();
        SetStatus("Controller emulation is off", false);
        _activationCancellation?.Dispose();
        _activationCancellation = null;
        _activationTask = null;
        _controllerToggle.Enabled = true;
    }

    private void SetControllerType(bool isDualSense)
    {
        if (InvokeRequired)
        {
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(new Action(() => SetControllerType(isDualSense)));
            }

            return;
        }

        _triggerVibrationToggle.Checked = false;
        _triggerVibrationToggle.Enabled = isDualSense;
        ControllerManager.SetAdaptiveTriggerVibrationEnabled(false);
        SetStatus(
            $"{(isDualSense ? "DualSense" : "DualShock 4")} connected · Xbox 360 emulation active",
            true);
    }

    private void CleanupController()
    {
        ControllerManager.StopListening();
        try
        {
            CloakManager.UnhideController();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to restore controller visibility: {ex}");
            CrashReporter.Report("Failed to restore controller visibility during cleanup", ex);
            SetStatus($"Unable to restore controller: {ex.Message}", false);
        }
        finally
        {
            XboxEmulator.Shutdown();
        }
    }

    private void SetStatus(string message, bool active)
    {
        if (InvokeRequired)
        {
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(new Action(() => SetStatus(message, active)));
            }

            return;
        }

        _statusLabel.Text = message;
        _statusIndicator.ForeColor = active
            ? Color.FromArgb(76, 214, 148)
            : Color.FromArgb(244, 181, 75);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        _triggerVibrationToggle.Checked = false;
        ControllerManager.SetAdaptiveTriggerVibrationEnabled(false);
        _activationCancellation?.Cancel();
        ControllerManager.StopListening();

        try
        {
            _activationTask?.GetAwaiter().GetResult();
            CleanupController();
        }
        catch (Exception ex)
        {
            CrashReporter.Report("Failed while closing the application", ex);
            MessageBox.Show(
                this,
                $"The controller could not be shut down cleanly: {ex.Message}",
                "DualSense Companion",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _activationCancellation?.Dispose();
            _activationCancellation = null;
        }

        base.OnFormClosing(e);
    }
}

class ToggleSwitch : Control
{
    private bool _checked;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.CheckButton;
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (Enabled)
        {
            Checked = !Checked;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Enabled && e.KeyCode == Keys.Space)
        {
            Checked = !Checked;
            e.Handled = true;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var trackColor = !Enabled
            ? Color.FromArgb(57, 67, 82)
            : Checked
                ? Color.FromArgb(61, 190, 139)
                : Color.FromArgb(75, 88, 108);
        using var trackBrush = new SolidBrush(trackColor);
        e.Graphics.FillEllipse(trackBrush, 0, 0, Height, Height);
        e.Graphics.FillEllipse(trackBrush, Width - Height, 0, Height, Height);
        e.Graphics.FillRectangle(trackBrush, Height / 2, 0, Width - Height, Height);

        var thumbSize = Height - 6;
        var thumbX = Checked ? Width - thumbSize - 3 : 3;
        using var thumbBrush = new SolidBrush(Enabled
            ? Color.White
            : Color.FromArgb(170, 177, 188));
        e.Graphics.FillEllipse(thumbBrush, thumbX, 3, thumbSize, thumbSize);
    }
}
