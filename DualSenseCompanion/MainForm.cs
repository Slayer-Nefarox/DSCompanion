using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly ToggleSwitch _controllerToggle;
    private readonly ToggleSwitch _triggerVibrationToggle;
    private readonly TrackBar _leftDeadzoneSlider = new();
    private readonly TrackBar _rightDeadzoneSlider = new();
    private readonly Label _leftDeadzoneValue = new();
    private readonly Label _rightDeadzoneValue = new();
    private readonly Label _statusLabel = new();
    private readonly Label _statusIndicator = new();
    private CancellationTokenSource? _activationCancellation;
    private Task? _activationTask;
    private bool _updatingSettings;
    private bool _settingsSaveErrorShown;
    private bool _closing;

    public MainForm()
    {
        _settings = AppSettings.Load(out var settingsLoadError, out var configExists);
        _controllerToggle = new ToggleSwitch
        {
            Checked = _settings.ControllerEmulationEnabled
        };
        _triggerVibrationToggle = new ToggleSwitch { Enabled = false };
        ControllerManager.LeftJoystickDeadzonePercent = _settings.LeftJoystickDeadzonePercent;
        ControllerManager.RightJoystickDeadzonePercent = _settings.RightJoystickDeadzonePercent;
        ControllerManager.SetButtonMappings(_settings.ButtonMappings);
        if (_settings.LightbarColor is Color configuredLightbarColor)
        {
            ControllerManager.SetLightbarColor(configuredLightbarColor);
        }

        if (settingsLoadError != null)
        {
            CrashReporter.Report("Failed to load application settings", new InvalidDataException(settingsLoadError));
            MessageBox.Show(
                settingsLoadError,
                "DualSense Companion settings",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

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
            Bounds = new Rectangle(130, 190, 700, 430)
        };
        Controls.Add(card);

        var optionsPage = new Panel
        {
            BackColor = Color.FromArgb(238, 15, 23, 40),
            Bounds = new Rectangle(130, 190, 700, 430),
            Visible = false
        };
        Controls.Add(optionsPage);

        var buttonMappingPage = new Panel
        {
            BackColor = Color.FromArgb(238, 15, 23, 40),
            Bounds = new Rectangle(130, 190, 700, 430),
            Visible = false
        };
        Controls.Add(buttonMappingPage);

        var heading = new Label
        {
            AutoSize = true,
            Text = "Controller setup",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 18F),
            Location = new Point(32, 24)
        };
        card.Controls.Add(heading);

        var moreOptionsButton = CreateNavigationButton("More options");
        moreOptionsButton.Location = new Point(520, 24);
        moreOptionsButton.Click += (_, _) =>
        {
            card.Visible = false;
            optionsPage.Visible = true;
        };
        card.Controls.Add(moreOptionsButton);

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

        var optionsHeading = new Label
        {
            AutoSize = true,
            Text = "More options",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 18F),
            Location = new Point(32, 24)
        };
        optionsPage.Controls.Add(optionsHeading);

        var backButton = CreateNavigationButton("Back");
        backButton.Location = new Point(560, 24);
        backButton.Click += (_, _) =>
        {
            optionsPage.Visible = false;
            card.Visible = true;
        };
        optionsPage.Controls.Add(backButton);

        var buttonMappingButton = CreateNavigationButton("Button map");
        buttonMappingButton.Bounds = new Rectangle(420, 24, 124, 40);
        buttonMappingButton.Click += (_, _) =>
        {
            optionsPage.Visible = false;
            buttonMappingPage.Visible = true;
        };
        optionsPage.Controls.Add(buttonMappingButton);

        var optionsDescription = new Label
        {
            AutoSize = true,
            Text = "Tune how controller input is mapped.",
            ForeColor = Color.FromArgb(170, 184, 205),
            Location = new Point(34, 64)
        };
        optionsPage.Controls.Add(optionsDescription);

        var buttonMappingHeading = new Label
        {
            AutoSize = true,
            Text = "Button remapping",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 18F),
            Location = new Point(32, 24)
        };
        buttonMappingPage.Controls.Add(buttonMappingHeading);

        var buttonMappingBackButton = CreateNavigationButton("Back");
        buttonMappingBackButton.Location = new Point(560, 24);
        buttonMappingBackButton.Click += (_, _) =>
        {
            buttonMappingPage.Visible = false;
            optionsPage.Visible = true;
        };
        buttonMappingPage.Controls.Add(buttonMappingBackButton);

        var buttonMappingDescription = new Label
        {
            AutoSize = true,
            Text = "Choose the Xbox 360 button emitted for each PlayStation digital button.",
            ForeColor = Color.FromArgb(170, 184, 205),
            Location = new Point(34, 64)
        };
        buttonMappingPage.Controls.Add(buttonMappingDescription);

        for (var index = 0; index < ButtonRemapping.PlayStationButtons.Count; index++)
        {
            AddButtonMappingRow(
                buttonMappingPage,
                ButtonRemapping.PlayStationButtons[index],
                index % 2,
                index / 2);
        }

        var deadzoneHeading = new Label
        {
            AutoSize = true,
            Text = "Left joystick deadzone",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11F),
            Location = new Point(34, 112)
        };
        optionsPage.Controls.Add(deadzoneHeading);

        var leftDeadzoneDescription = new Label
        {
            AutoSize = true,
            Text = "Radial center deadzone for the left analog stick.",
            ForeColor = Color.FromArgb(157, 172, 194),
            Location = new Point(34, 139)
        };
        optionsPage.Controls.Add(leftDeadzoneDescription);

        AddDeadzoneSlider(
            optionsPage,
            _leftDeadzoneSlider,
            _leftDeadzoneValue,
            _settings.LeftJoystickDeadzonePercent,
            "Left joystick deadzone",
            165);

        var rightDeadzoneHeading = new Label
        {
            AutoSize = true,
            Text = "Right joystick deadzone",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11F),
            Location = new Point(34, 232)
        };
        optionsPage.Controls.Add(rightDeadzoneHeading);

        var rightDeadzoneDescription = new Label
        {
            AutoSize = true,
            Text = "Radial center deadzone for the right analog stick.",
            ForeColor = Color.FromArgb(157, 172, 194),
            Location = new Point(34, 259)
        };
        optionsPage.Controls.Add(rightDeadzoneDescription);

        AddDeadzoneSlider(
            optionsPage,
            _rightDeadzoneSlider,
            _rightDeadzoneValue,
            _settings.RightJoystickDeadzonePercent,
            "Right joystick deadzone",
            285);

        var lightbarColorHeading = new Label
        {
            AutoSize = true,
            Text = "Controller lightbar color",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11F),
            Location = new Point(34, 350)
        };
        optionsPage.Controls.Add(lightbarColorHeading);

        var lightbarColorButton = new Button
        {
            Text = "Choose color",
            Bounds = new Rectangle(34, 378, 145, 38),
            BackColor = Color.FromArgb(37, 99, 174),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 9F),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        optionsPage.Controls.Add(lightbarColorButton);

        var lightbarColorPreview = new Panel
        {
            BackColor = _settings.LightbarColor ?? Color.FromArgb(75, 88, 108),
            Bounds = new Rectangle(195, 380, 40, 34),
            BorderStyle = BorderStyle.FixedSingle
        };
        optionsPage.Controls.Add(lightbarColorPreview);

        var lightbarColorValue = new Label
        {
            AutoSize = true,
            Text = _settings.LightbarColor is Color selectedColor
                ? $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}"
                : "Not set",
            ForeColor = Color.FromArgb(205, 215, 230),
            Location = new Point(250, 389)
        };
        optionsPage.Controls.Add(lightbarColorValue);

        _controllerToggle.CheckedChanged += ControllerToggle_CheckedChanged;
        _triggerVibrationToggle.CheckedChanged += (_, _) =>
        {
            if (!_updatingSettings)
            {
                _settings.AdaptiveTriggerVibrationEnabled = _triggerVibrationToggle.Checked;
                SaveSettings();
            }

            ControllerManager.SetAdaptiveTriggerVibrationEnabled(_triggerVibrationToggle.Checked);
        };
        _leftDeadzoneSlider.ValueChanged += (_, _) =>
        {
            _settings.LeftJoystickDeadzonePercent = _leftDeadzoneSlider.Value;
            _leftDeadzoneValue.Text = $"{_leftDeadzoneSlider.Value}%";
            ControllerManager.LeftJoystickDeadzonePercent = _leftDeadzoneSlider.Value;
            SaveSettings();
        };
        _rightDeadzoneSlider.ValueChanged += (_, _) =>
        {
            _settings.RightJoystickDeadzonePercent = _rightDeadzoneSlider.Value;
            _rightDeadzoneValue.Text = $"{_rightDeadzoneSlider.Value}%";
            ControllerManager.RightJoystickDeadzonePercent = _rightDeadzoneSlider.Value;
            SaveSettings();
        };
        lightbarColorButton.Click += (_, _) =>
        {
            using var colorDialog = new ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                Color = _settings.LightbarColor ?? Color.White
            };

            if (colorDialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var color = Color.FromArgb(255, colorDialog.Color.R, colorDialog.Color.G, colorDialog.Color.B);
            _settings.LightbarColor = color;
            lightbarColorPreview.BackColor = color;
            lightbarColorValue.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            ControllerManager.SetLightbarColor(color);
            SaveSettings();
        };
        Shown += (_, _) =>
        {
            if (_controllerToggle.Checked)
            {
                StartController();
            }
            else
            {
                SetStatus("Controller emulation is off", false);
            }
        };

        if (!configExists && settingsLoadError == null)
        {
            SaveSettings();
        }
    }

    private static Button CreateNavigationButton(string text)
    {
        return new Button
        {
            Text = text,
            Bounds = new Rectangle(0, 0, 108, 40),
            BackColor = Color.FromArgb(37, 99, 174),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 9F),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
    }

    private static void AddDeadzoneSlider(
        Control parent,
        TrackBar slider,
        Label valueLabel,
        int value,
        string accessibleName,
        int top)
    {
        slider.Minimum = 0;
        slider.Maximum = AppSettings.MaximumJoystickDeadzonePercent;
        slider.Value = value;
        slider.TickFrequency = 5;
        slider.SmallChange = 1;
        slider.LargeChange = 5;
        slider.Bounds = new Rectangle(28, top, 450, 55);
        slider.AccessibleName = $"{accessibleName} percentage";
        parent.Controls.Add(slider);

        valueLabel.AutoSize = true;
        valueLabel.Text = $"{value}%";
        valueLabel.ForeColor = Color.White;
        valueLabel.Font = new Font("Segoe UI Semibold", 12F);
        valueLabel.Location = new Point(510, top + 12);
        parent.Controls.Add(valueLabel);
    }

    private void AddButtonMappingRow(
        Control parent,
        PlayStationButton source,
        int column,
        int row)
    {
        var columnLeft = column == 0 ? 32 : 350;
        var top = 104 + row * 35;
        var label = new Label
        {
            AutoSize = false,
            Text = ButtonRemapping.GetDisplayName(source),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            Bounds = new Rectangle(columnLeft, top, 105, 26)
        };
        parent.Controls.Add(label);

        var comboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Bounds = new Rectangle(columnLeft + 108, top, 178, 26),
            AccessibleName = $"{ButtonRemapping.GetDisplayName(source)} mapping"
        };
        foreach (var target in ButtonRemapping.XboxButtons)
        {
            comboBox.Items.Add(target);
        }

        var currentTarget = _settings.ButtonMappings[source];
        comboBox.SelectedItem = ButtonRemapping.XboxButtons
            .First(option => option.Value == currentTarget);
        comboBox.SelectedIndexChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is not XboxButtonOption selectedTarget)
            {
                return;
            }

            _settings.ButtonMappings[source] = selectedTarget.Value;
            ControllerManager.SetButtonMapping(source, selectedTarget.Value);
            SaveSettings();
        };
        parent.Controls.Add(comboBox);
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
        _settings.ControllerEmulationEnabled = _controllerToggle.Checked;
        SaveSettings();

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
        SetTriggerVibrationToggle(false, false);
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
        SetTriggerVibrationToggle(false, false);
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

        SetTriggerVibrationToggle(
            isDualSense && _settings.AdaptiveTriggerVibrationEnabled,
            isDualSense);
        if (_settings.LightbarColor is Color lightbarColor)
        {
            ControllerManager.SetLightbarColor(lightbarColor);
        }

        SetStatus(
            $"{(isDualSense ? "DualSense" : "DualShock 4")} connected · Xbox 360 emulation active",
            true);
    }

    private void SetTriggerVibrationToggle(bool isChecked, bool isEnabled)
    {
        _updatingSettings = true;
        try
        {
            _triggerVibrationToggle.Checked = isChecked;
            _triggerVibrationToggle.Enabled = isEnabled;
        }
        finally
        {
            _updatingSettings = false;
        }

        ControllerManager.SetAdaptiveTriggerVibrationEnabled(isChecked);
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save();
            _settingsSaveErrorShown = false;
        }
        catch (IOException ex)
        {
            ReportSettingsSaveFailure(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            ReportSettingsSaveFailure(ex);
        }
    }

    private void ReportSettingsSaveFailure(Exception exception)
    {
        CrashReporter.Report("Failed to save application settings", exception);
        if (_settingsSaveErrorShown)
        {
            return;
        }

        _settingsSaveErrorShown = true;
        MessageBox.Show(
            this,
            $"Settings could not be saved to {AppSettings.ConfigPath}: {exception.Message}",
            "DualSense Companion settings",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
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
        SetTriggerVibrationToggle(false, false);
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
