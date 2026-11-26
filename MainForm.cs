using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

// Janela principal: constrói a interface, salva opções e inicia ou para o controle virtual.
class MainForm : Form
{
    private const int DesignWidth = 960;
    private const int DesignHeight = 640;
    // Referência às configurações persistidas pelo usuário.
    private readonly AppSettings _settings;
    // Interruptores de ativação e vibração adaptativa.
    private readonly ToggleSwitch _controllerToggle;
    private readonly ToggleSwitch _triggerVibrationToggle;
    private readonly Button _controllerSelectionButton;
    private readonly ListBox _controllerSelectionList = new();
    private readonly Label _controllerSelectionStatus = new();
    private readonly Button _controllerSelectionApplyButton = new();
    private readonly Dictionary<PlayStationButton, Label> _buttonMappingLabels = new();
    // Barra que define a intensidade aplicada aos gatilhos adaptativos.
    private readonly TrackBar _triggerIntensitySlider = new();
    // Barras usadas para alterar zonas neutras internas.
    private readonly TrackBar _leftDeadzoneSlider = new();
    private readonly TrackBar _rightDeadzoneSlider = new();
    // Barras usadas para alterar limites externos dos analógicos.
    private readonly TrackBar _leftAntiDeadzoneSlider = new();
    private readonly TrackBar _rightAntiDeadzoneSlider = new();
    // Barras usadas para ajustar as zonas mortas e compensações de L2 e R2.
    private readonly TrackBar _leftTriggerDeadzoneSlider = new();
    private readonly TrackBar _rightTriggerDeadzoneSlider = new();
    private readonly TrackBar _leftTriggerAntiDeadzoneSlider = new();
    private readonly TrackBar _rightTriggerAntiDeadzoneSlider = new();
    // Rótulos que mostram os valores das deadzones e da intensidade dos gatilhos.
    private readonly Label _leftDeadzoneValue = new();
    private readonly Label _rightDeadzoneValue = new();
    private readonly Label _leftAntiDeadzoneValue = new();
    private readonly Label _rightAntiDeadzoneValue = new();
    private readonly Label _leftTriggerDeadzoneValue = new();
    private readonly Label _rightTriggerDeadzoneValue = new();
    private readonly Label _leftTriggerAntiDeadzoneValue = new();
    private readonly Label _rightTriggerAntiDeadzoneValue = new();
    private readonly Label _triggerIntensityValue = new();
    private readonly Label _statusLabel = new();
    // Indicador circular que informa visualmente se o emulador está ativo.
    private readonly Label _statusIndicator = new();
    // Controles usados para selecionar perfil e atualizar os ComboBoxes ao trocar de slot.
    private ComboBox? _configurationSlotComboBox;
    private Panel? _lightbarColorPreview;
    private Label? _lightbarColorValue;
    private Label? _lightbarColorHeading;
    private Label? _buttonMappingDescription;
    private Button? _adaptiveTriggerSettingsButton;
    private Button? _lightbarColorButton;
    private readonly Dictionary<PlayStationButton, ComboBox> _buttonMappingComboBoxes = new();
    // Origem usada para cancelar a tarefa assíncrona de ativação.
    private CancellationTokenSource? _activationCancellation;
    // Tarefa responsável pela inicialização e monitoramento do controle.
    private Task? _activationTask;
    // Impede o salvamento durante uma atualização feita pelo próprio programa.
    private bool _updatingSettings;
    // Evita repetir o mesmo aviso de falha ao salvar.
    private bool _settingsSaveErrorShown;
    // Indica que a janela está em processo de fechamento.
    private bool _closing;
    private Panel? _resizableContentPanel;
    private float _contentScale = 1F;

    public MainForm()
    {
        // Construtor responsável por criar todo o painel visual e conectar seus eventos.
        // Recupera as opções persistentes antes de criar os controles visuais.
        _settings = AppSettings.Load(out var settingsLoadError, out var configExists);
        // Criando os dois interruptores principais com seus estados iniciais.
        _controllerToggle = new ToggleSwitch
        {
            Checked = _settings.ControllerEmulationEnabled
        };
        _triggerVibrationToggle = new ToggleSwitch { Enabled = false };
        ControllerManager.LeftJoystickDeadzonePercent = _settings.LeftJoystickDeadzonePercent;
        ControllerManager.RightJoystickDeadzonePercent = _settings.RightJoystickDeadzonePercent;
        ControllerManager.LeftJoystickAntiDeadzonePercent = _settings.LeftJoystickAntiDeadzonePercent;
        ControllerManager.RightJoystickAntiDeadzonePercent = _settings.RightJoystickAntiDeadzonePercent;
        ControllerManager.LeftTriggerDeadzonePercent = _settings.LeftTriggerDeadzonePercent;
        ControllerManager.RightTriggerDeadzonePercent = _settings.RightTriggerDeadzonePercent;
        ControllerManager.LeftTriggerAntiDeadzonePercent = _settings.LeftTriggerAntiDeadzonePercent;
        ControllerManager.RightTriggerAntiDeadzonePercent = _settings.RightTriggerAntiDeadzonePercent;
        ControllerManager.SetAdaptiveTriggerIntensityPercent(_settings.AdaptiveTriggerIntensityPercent);
        ControllerManager.SetActiveConfigurationSlot(_settings.ActiveSlot);
        ControllerManager.SetButtonMappings(_settings.ButtonMappings);
        ControllerManager.SetConfigMapMappings(_settings.ConfigMapMappings);
        // Troca dinâmica acionada por um botão físico marcado como Config map.
        ControllerManager.SetConfigurationSlotHandler(SwitchConfigurationSlot);
        // Aplica a cor configurada antes da primeira operação de vibração.
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
        // Define o tamanho inicial e a aparência geral da janela.
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimumSize = new Size(800, 540);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(DesignWidth, DesignHeight);
        BackColor = Color.FromArgb(11, 16, 29);
        Font = new Font("Segoe UI", 10F);
        // Carrega a imagem de fundo a partir da pasta de execução.
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

        // Cria o cartão principal que contém o estado e as opções básicas.
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

        var triggerDeadzonePage = new Panel
        {
            BackColor = Color.FromArgb(238, 15, 23, 40),
            Bounds = new Rectangle(130, 190, 700, 430),
            Visible = false
        };
        Controls.Add(triggerDeadzonePage);

        var adaptiveTriggerPage = new Panel
        {
            BackColor = Color.FromArgb(238, 15, 23, 40),
            Bounds = new Rectangle(130, 190, 700, 430),
            Visible = false
        };
        Controls.Add(adaptiveTriggerPage);

        var controllerSelectionPage = new Panel
        {
            BackColor = Color.FromArgb(238, 15, 23, 40),
            Bounds = new Rectangle(130, 190, 700, 430),
            Visible = false
        };
        Controls.Add(controllerSelectionPage);

        var attributionLabel = new Label
        {
            AutoSize = false,
            Text = "by Slayer Nefarox",
            ForeColor = Color.FromArgb(205, 215, 230),
            Font = new Font("Segoe UI", 8.5F),
            TextAlign = ContentAlignment.MiddleLeft,
            Bounds = new Rectangle(16, 620, 200, 19),
            BackColor = Color.Transparent
        };
        Controls.Add(attributionLabel);

        var versionLabel = new Label
        {
            AutoSize = false,
            Text = "version: 2.0",
            ForeColor = Color.FromArgb(205, 215, 230),
            Font = new Font("Segoe UI", 8.5F),
            TextAlign = ContentAlignment.MiddleRight,
            Bounds = new Rectangle(744, 620, 200, 19),
            BackColor = Color.Transparent
        };
        Controls.Add(versionLabel);

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
        // Navega da tela principal para o painel de opções avançadas.
        moreOptionsButton.Click += (_, _) =>
        {
            card.Visible = false;
            optionsPage.Visible = true;
        };
        card.Controls.Add(moreOptionsButton);

        var description = new Label
        {
            AutoSize = true,
            Text = "Manage your controller-to-Xbox bridge.",
            ForeColor = Color.FromArgb(170, 184, 205),
            Location = new Point(34, 64)
        };
        card.Controls.Add(description);

        AddSettingRow(
            card,
            "Controller emulation",
            "Hide the selected controller and emulate an Xbox 360 controller.",
            115,
            _controllerToggle);

        _controllerSelectionButton = CreateNavigationButton("Select");
        _controllerSelectionButton.Bounds = new Rectangle(470, 115, 108, 40);
        _controllerSelectionButton.AccessibleName = "Select PlayStation or XInput controller";
        _controllerSelectionButton.Click += (_, _) =>
        {
            RefreshControllerSelectionList();
            card.Visible = false;
            controllerSelectionPage.Visible = true;
        };
        card.Controls.Add(_controllerSelectionButton);

        // A página separada permite confirmar o dispositivo antes de alterar a seleção salva.
        var controllerSelectionHeading = new Label
        {
            AutoSize = true,
            Text = "Select controller",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 18F),
            Location = new Point(32, 24)
        };
        controllerSelectionPage.Controls.Add(controllerSelectionHeading);

        var controllerSelectionDescription = new Label
        {
            AutoSize = true,
            Text = "Choose a connected PlayStation or XInput controller to use and hide.",
            ForeColor = Color.FromArgb(170, 184, 205),
            Location = new Point(34, 64)
        };
        controllerSelectionPage.Controls.Add(controllerSelectionDescription);

        _controllerSelectionList.Bounds = new Rectangle(34, 110, 632, 218);
        _controllerSelectionList.AccessibleName = "Connected compatible controllers";
        _controllerSelectionList.BackColor = Color.FromArgb(24, 33, 50);
        _controllerSelectionList.ForeColor = Color.White;
        _controllerSelectionList.BorderStyle = BorderStyle.FixedSingle;
        _controllerSelectionList.IntegralHeight = false;
        _controllerSelectionList.SelectedIndexChanged += (_, _) =>
        {
            _controllerSelectionApplyButton.Enabled =
                _controllerSelectionList.SelectedItem is ControllerSelectionOption;
        };
        controllerSelectionPage.Controls.Add(_controllerSelectionList);

        _controllerSelectionStatus.AutoSize = false;
        _controllerSelectionStatus.Bounds = new Rectangle(34, 338, 610, 22);
        _controllerSelectionStatus.ForeColor = Color.FromArgb(205, 215, 230);
        controllerSelectionPage.Controls.Add(_controllerSelectionStatus);

        var refreshControllersButton = CreateNavigationButton("Refresh");
        refreshControllersButton.Location = new Point(34, 370);
        refreshControllersButton.Click += (_, _) => RefreshControllerSelectionList();
        controllerSelectionPage.Controls.Add(refreshControllersButton);

        var cancelControllerSelectionButton = CreateNavigationButton("Cancel");
        cancelControllerSelectionButton.Location = new Point(440, 370);
        cancelControllerSelectionButton.Click += (_, _) =>
        {
            controllerSelectionPage.Visible = false;
            card.Visible = true;
        };
        controllerSelectionPage.Controls.Add(cancelControllerSelectionButton);

        _controllerSelectionApplyButton.Text = "Apply";
        _controllerSelectionApplyButton.Bounds = new Rectangle(558, 370, 108, 40);
        _controllerSelectionApplyButton.BackColor = Color.FromArgb(37, 99, 174);
        _controllerSelectionApplyButton.ForeColor = Color.White;
        _controllerSelectionApplyButton.FlatStyle = FlatStyle.Flat;
        _controllerSelectionApplyButton.Font = new Font("Segoe UI Semibold", 9F);
        _controllerSelectionApplyButton.Cursor = Cursors.Hand;
        _controllerSelectionApplyButton.UseVisualStyleBackColor = false;
        _controllerSelectionApplyButton.Enabled = false;
        _controllerSelectionApplyButton.Click += (_, _) =>
        {
            if (_controllerSelectionList.SelectedItem is not ControllerSelectionOption selectedController)
            {
                return;
            }

            var previousInstanceId = _settings.SelectedControllerInstanceId;
            _settings.SelectedControllerInstanceId = selectedController.InstanceId;
            if (!SaveSettings())
            {
                _settings.SelectedControllerInstanceId = previousInstanceId;
                return;
            }

            UpdateInputSourceAvailability(
                selectedController.Kind == ControllerConnectionKind.XInput);
            controllerSelectionPage.Visible = false;
            card.Visible = true;
        };
        controllerSelectionPage.Controls.Add(_controllerSelectionApplyButton);

        // As páginas avançadas separam controles principais, deadzones e remapeamento.
        var triggerSettingsTitle = new Label
        {
            AutoSize = true,
            Text = "Adaptive-trigger settings",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11F),
            Location = new Point(34, 205)
        };
        card.Controls.Add(triggerSettingsTitle);

        var triggerSettingsDescription = new Label
        {
            AutoSize = true,
            Text = "Configure rumble feedback for DualSense triggers.",
            ForeColor = Color.FromArgb(157, 172, 194),
            Location = new Point(34, 234)
        };
        card.Controls.Add(triggerSettingsDescription);

        _adaptiveTriggerSettingsButton = CreateNavigationButton("Open");
        _adaptiveTriggerSettingsButton.Location = new Point(560, 210);
        _adaptiveTriggerSettingsButton.Click += (_, _) =>
        {
            card.Visible = false;
            adaptiveTriggerPage.Visible = true;
        };
        card.Controls.Add(_adaptiveTriggerSettingsButton);

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

        var triggerDeadzoneButton = CreateNavigationButton("Trigger deadzones");
        triggerDeadzoneButton.Bounds = new Rectangle(276, 24, 136, 40);
        triggerDeadzoneButton.Click += (_, _) =>
        {
            optionsPage.Visible = false;
            triggerDeadzonePage.Visible = true;
        };
        optionsPage.Controls.Add(triggerDeadzoneButton);

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

        var triggerDeadzoneHeading = new Label
        {
            AutoSize = true,
            Text = "Trigger deadzones",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 18F),
            Location = new Point(32, 24)
        };
        triggerDeadzonePage.Controls.Add(triggerDeadzoneHeading);

        var triggerDeadzoneBackButton = CreateNavigationButton("Back");
        triggerDeadzoneBackButton.Location = new Point(560, 24);
        triggerDeadzoneBackButton.Click += (_, _) =>
        {
            triggerDeadzonePage.Visible = false;
            optionsPage.Visible = true;
        };
        triggerDeadzonePage.Controls.Add(triggerDeadzoneBackButton);

        var triggerDeadzoneDescription = new Label
        {
            AutoSize = true,
            Text = "Adjust the analog input range of L2 and R2 independently.",
            ForeColor = Color.FromArgb(170, 184, 205),
            Location = new Point(34, 64)
        };
        triggerDeadzonePage.Controls.Add(triggerDeadzoneDescription);

        var leftTriggerDeadzoneHeading = new Label
        {
            AutoSize = true,
            Text = "Left trigger deadzone",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11F),
            Location = new Point(34, 112)
        };
        triggerDeadzonePage.Controls.Add(leftTriggerDeadzoneHeading);

        var leftTriggerDeadzoneDescription = new Label
        {
            AutoSize = true,
            Text = "Ignore small L2 inputs near release.",
            ForeColor = Color.FromArgb(157, 172, 194),
            Location = new Point(34, 139)
        };
        triggerDeadzonePage.Controls.Add(leftTriggerDeadzoneDescription);

        AddDeadzoneSlider(
            triggerDeadzonePage,
            _leftTriggerDeadzoneSlider,
            _leftTriggerDeadzoneValue,
            _settings.LeftTriggerDeadzonePercent,
            "Left trigger deadzone",
            165,
            34,
            280,
            AppSettings.MaximumTriggerDeadzonePercent,
            valueBeforeSlider: true);

        var leftTriggerAntiDeadzoneHeading = new Label
        {
            AutoSize = true,
            Text = "Left trigger anti-deadzone",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11F),
            Location = new Point(350, 112)
        };
        triggerDeadzonePage.Controls.Add(leftTriggerAntiDeadzoneHeading);

        var leftTriggerAntiDeadzoneDescription = new Label
        {
            AutoSize = true,
            Text = "Reach full output before L2 is fully pressed.",
            ForeColor = Color.FromArgb(157, 172, 194),
            Location = new Point(350, 139)
        };
        triggerDeadzonePage.Controls.Add(leftTriggerAntiDeadzoneDescription);

        AddDeadzoneSlider(
            triggerDeadzonePage,
            _leftTriggerAntiDeadzoneSlider,
            _leftTriggerAntiDeadzoneValue,
            _settings.LeftTriggerAntiDeadzonePercent,
            "Left trigger anti-deadzone",
            165,
            350,
            280,
            AppSettings.MaximumTriggerDeadzonePercent);

        var rightTriggerDeadzoneHeading = new Label
        {
            AutoSize = true,
            Text = "Right trigger deadzone",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11F),
            Location = new Point(34, 232)
        };
        triggerDeadzonePage.Controls.Add(rightTriggerDeadzoneHeading);

        var rightTriggerDeadzoneDescription = new Label
        {
            AutoSize = true,
            Text = "Ignore small R2 inputs near release.",
            ForeColor = Color.FromArgb(157, 172, 194),
            Location = new Point(34, 259)
        };
        triggerDeadzonePage.Controls.Add(rightTriggerDeadzoneDescription);

        AddDeadzoneSlider(
            triggerDeadzonePage,
            _rightTriggerDeadzoneSlider,
            _rightTriggerDeadzoneValue,
            _settings.RightTriggerDeadzonePercent,
            "Right trigger deadzone",
            285,
            34,
            280,
            AppSettings.MaximumTriggerDeadzonePercent,
            valueBeforeSlider: true);

        var rightTriggerAntiDeadzoneHeading = new Label
        {
            AutoSize = true,
            Text = "Right trigger anti-deadzone",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11F),
            Location = new Point(350, 232)
        };
        triggerDeadzonePage.Controls.Add(rightTriggerAntiDeadzoneHeading);

        var rightTriggerAntiDeadzoneDescription = new Label
        {
            AutoSize = true,
            Text = "Reach full output before R2 is fully pressed.",
            ForeColor = Color.FromArgb(157, 172, 194),
            Location = new Point(350, 259)
        };
        triggerDeadzonePage.Controls.Add(rightTriggerAntiDeadzoneDescription);

        AddDeadzoneSlider(
            triggerDeadzonePage,
            _rightTriggerAntiDeadzoneSlider,
            _rightTriggerAntiDeadzoneValue,
            _settings.RightTriggerAntiDeadzonePercent,
            "Right trigger anti-deadzone",
            285,
            350,
            280,
            AppSettings.MaximumTriggerDeadzonePercent);

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

        _buttonMappingDescription = new Label
        {
            AutoSize = true,
            Text = "Choose the Xbox 360 button emitted for each PlayStation digital button.",
            ForeColor = Color.FromArgb(170, 184, 205),
            Location = new Point(34, 64)
        };
        buttonMappingPage.Controls.Add(_buttonMappingDescription);

        // Seletor manual do perfil. Config map pode alternar o mesmo slot por meio do controle.
        var slotLabel = new Label
        {
            AutoSize = true,
            Text = "Configuration slot",
            ForeColor = Color.FromArgb(205, 215, 230),
            Location = new Point(34, 92)
        };
        buttonMappingPage.Controls.Add(slotLabel);

        _configurationSlotComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Bounds = new Rectangle(160, 88, 150, 26),
            AccessibleName = "Configuration slot"
        };
        for (var slot = 1; slot <= AppSettings.ConfigurationSlotCount; slot++)
        {
            _configurationSlotComboBox.Items.Add($"Slot {slot}");
        }
        _configurationSlotComboBox.SelectedIndex = _settings.ActiveSlot - 1;
        _configurationSlotComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (_configurationSlotComboBox.SelectedIndex >= 0)
            {
                SwitchConfigurationSlot(_configurationSlotComboBox.SelectedIndex + 1);
            }
        };
        buttonMappingPage.Controls.Add(_configurationSlotComboBox);

        var adaptiveTriggerHeading = new Label
        {
            AutoSize = true,
            Text = "Adaptive triggers",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 18F),
            Location = new Point(32, 24)
        };
        adaptiveTriggerPage.Controls.Add(adaptiveTriggerHeading);

        var adaptiveTriggerBackButton = CreateNavigationButton("Back");
        adaptiveTriggerBackButton.Location = new Point(560, 24);
        adaptiveTriggerBackButton.Click += (_, _) =>
        {
            adaptiveTriggerPage.Visible = false;
            card.Visible = true;
        };
        adaptiveTriggerPage.Controls.Add(adaptiveTriggerBackButton);

        var adaptiveTriggerDescription = new Label
        {
            AutoSize = true,
            Text = "Mirror Xbox rumble into both DualSense triggers.",
            ForeColor = Color.FromArgb(170, 184, 205),
            Location = new Point(34, 64)
        };
        adaptiveTriggerPage.Controls.Add(adaptiveTriggerDescription);

        AddSettingRow(
            adaptiveTriggerPage,
            "Adaptive-trigger vibration",
            "Enable rumble feedback on DualSense triggers.",
            112,
            _triggerVibrationToggle);

        var triggerIntensityHeading = new Label
        {
            AutoSize = true,
            Text = "Trigger feedback intensity",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11F),
            Location = new Point(34, 210)
        };
        adaptiveTriggerPage.Controls.Add(triggerIntensityHeading);

        var triggerIntensityDescription = new Label
        {
            AutoSize = true,
            Text = "0% follows the stronger rumble; 1–100% uses the selected strength whenever rumble is active.",
            ForeColor = Color.FromArgb(157, 172, 194),
            Location = new Point(34, 239)
        };
        adaptiveTriggerPage.Controls.Add(triggerIntensityDescription);

        AddDeadzoneSlider(
            adaptiveTriggerPage,
            _triggerIntensitySlider,
            _triggerIntensityValue,
            _settings.AdaptiveTriggerIntensityPercent,
            "Trigger feedback intensity",
            270,
            34,
            560,
            100);

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
            165,
            34,
            280,
            valueBeforeSlider: true);

        var leftAntiDeadzoneHeading = new Label
        {
            AutoSize = true,
            Text = "Left joystick anti-deadzone",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11F),
            Location = new Point(350, 112)
        };
        optionsPage.Controls.Add(leftAntiDeadzoneHeading);

        var leftAntiDeadzoneDescription = new Label
        {
            AutoSize = true,
            Text = "External radial limit for the left analog stick.",
            ForeColor = Color.FromArgb(157, 172, 194),
            Location = new Point(350, 139)
        };
        optionsPage.Controls.Add(leftAntiDeadzoneDescription);

        AddDeadzoneSlider(
            optionsPage,
            _leftAntiDeadzoneSlider,
            _leftAntiDeadzoneValue,
            _settings.LeftJoystickAntiDeadzonePercent,
            "Left joystick anti-deadzone",
            165,
            350,
            280);

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
            285,
            34,
            280,
            valueBeforeSlider: true);

        var rightAntiDeadzoneHeading = new Label
        {
            AutoSize = true,
            Text = "Right joystick anti-deadzone",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11F),
            Location = new Point(350, 232)
        };
        optionsPage.Controls.Add(rightAntiDeadzoneHeading);

        var rightAntiDeadzoneDescription = new Label
        {
            AutoSize = true,
            Text = "External radial limit for the right analog stick.",
            ForeColor = Color.FromArgb(157, 172, 194),
            Location = new Point(350, 259)
        };
        optionsPage.Controls.Add(rightAntiDeadzoneDescription);

        AddDeadzoneSlider(
            optionsPage,
            _rightAntiDeadzoneSlider,
            _rightAntiDeadzoneValue,
            _settings.RightJoystickAntiDeadzonePercent,
            "Right joystick anti-deadzone",
            285,
            350,
            280);

        _lightbarColorHeading = new Label
        {
            AutoSize = true,
            Text = "Controller lightbar color",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11F),
            Location = new Point(34, 350)
        };
        optionsPage.Controls.Add(_lightbarColorHeading);

        _lightbarColorButton = new Button
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
        optionsPage.Controls.Add(_lightbarColorButton);

        _lightbarColorPreview = new Panel
        {
            BackColor = _settings.LightbarColor ?? Color.FromArgb(75, 88, 108),
            Bounds = new Rectangle(195, 380, 40, 34),
            BorderStyle = BorderStyle.FixedSingle
        };
        optionsPage.Controls.Add(_lightbarColorPreview);

        _lightbarColorValue = new Label
        {
            AutoSize = true,
            Text = _settings.LightbarColor is Color selectedColor
                ? $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}"
                : "Not set",
            ForeColor = Color.FromArgb(205, 215, 230),
            Location = new Point(250, 389)
        };
        optionsPage.Controls.Add(_lightbarColorValue);

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
        _triggerIntensitySlider.ValueChanged += (_, _) =>
        {
            _settings.AdaptiveTriggerIntensityPercent = _triggerIntensitySlider.Value;
            _triggerIntensityValue.Text = $"{_triggerIntensitySlider.Value}%";
            ControllerManager.SetAdaptiveTriggerIntensityPercent(_triggerIntensitySlider.Value);
            SaveSettings();
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
        _leftAntiDeadzoneSlider.ValueChanged += (_, _) =>
        {
            _settings.LeftJoystickAntiDeadzonePercent = _leftAntiDeadzoneSlider.Value;
            _leftAntiDeadzoneValue.Text = $"{_leftAntiDeadzoneSlider.Value}%";
            ControllerManager.LeftJoystickAntiDeadzonePercent = _leftAntiDeadzoneSlider.Value;
            SaveSettings();
        };
        _rightAntiDeadzoneSlider.ValueChanged += (_, _) =>
        {
            _settings.RightJoystickAntiDeadzonePercent = _rightAntiDeadzoneSlider.Value;
            _rightAntiDeadzoneValue.Text = $"{_rightAntiDeadzoneSlider.Value}%";
            ControllerManager.RightJoystickAntiDeadzonePercent = _rightAntiDeadzoneSlider.Value;
            SaveSettings();
        };
        _leftTriggerDeadzoneSlider.ValueChanged += (_, _) =>
        {
            _settings.LeftTriggerDeadzonePercent = _leftTriggerDeadzoneSlider.Value;
            _leftTriggerDeadzoneValue.Text = $"{_leftTriggerDeadzoneSlider.Value}%";
            ControllerManager.LeftTriggerDeadzonePercent = _leftTriggerDeadzoneSlider.Value;
            SaveSettings();
        };
        _rightTriggerDeadzoneSlider.ValueChanged += (_, _) =>
        {
            _settings.RightTriggerDeadzonePercent = _rightTriggerDeadzoneSlider.Value;
            _rightTriggerDeadzoneValue.Text = $"{_rightTriggerDeadzoneSlider.Value}%";
            ControllerManager.RightTriggerDeadzonePercent = _rightTriggerDeadzoneSlider.Value;
            SaveSettings();
        };
        _leftTriggerAntiDeadzoneSlider.ValueChanged += (_, _) =>
        {
            _settings.LeftTriggerAntiDeadzonePercent = _leftTriggerAntiDeadzoneSlider.Value;
            _leftTriggerAntiDeadzoneValue.Text = $"{_leftTriggerAntiDeadzoneSlider.Value}%";
            ControllerManager.LeftTriggerAntiDeadzonePercent = _leftTriggerAntiDeadzoneSlider.Value;
            SaveSettings();
        };
        _rightTriggerAntiDeadzoneSlider.ValueChanged += (_, _) =>
        {
            _settings.RightTriggerAntiDeadzonePercent = _rightTriggerAntiDeadzoneSlider.Value;
            _rightTriggerAntiDeadzoneValue.Text = $"{_rightTriggerAntiDeadzoneSlider.Value}%";
            ControllerManager.RightTriggerAntiDeadzonePercent = _rightTriggerAntiDeadzoneSlider.Value;
            SaveSettings();
        };
        _lightbarColorButton.Click += (_, _) =>
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
            UpdateLightbarColorDisplay();
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

        UpdateInputSourceAvailability(
            XInputControllerManager.IsXInputSelection(_settings.SelectedControllerInstanceId));
        InitializeResizableLayout();
    }

    // Agrupa os controles em um painel-base para que a janela escale sem distorcer o layout.
    private void InitializeResizableLayout()
    {
        var contentPanel = new Panel
        {
            BackColor = Color.Transparent,
            Bounds = new Rectangle(0, 0, DesignWidth, DesignHeight),
            TabStop = false
        };

        foreach (Control control in Controls.Cast<Control>().ToArray())
        {
            contentPanel.Controls.Add(control);
        }

        Controls.Add(contentPanel);
        _resizableContentPanel = contentPanel;
        Resize += ResizeContent;
        ResizeContent(this, EventArgs.Empty);
    }

    // Mantém a proporção do desenho e centraliza o conteúdo no espaço disponível.
    private void ResizeContent(object? sender, EventArgs e)
    {
        if (_resizableContentPanel == null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        var scale = Math.Min(
            (float)ClientSize.Width / DesignWidth,
            (float)ClientSize.Height / DesignHeight);
        var scaleChange = scale / _contentScale;
        if (Math.Abs(scaleChange - 1F) > 0.001F)
        {
            _resizableContentPanel.Scale(new SizeF(scaleChange, scaleChange));
            _contentScale = scale;
        }

        var contentWidth = (int)Math.Round(DesignWidth * scale);
        var contentHeight = (int)Math.Round(DesignHeight * scale);
        _resizableContentPanel.Bounds = new Rectangle(
            (ClientSize.Width - contentWidth) / 2,
            (ClientSize.Height - contentHeight) / 2,
            contentWidth,
            contentHeight);
    }

    private static Button CreateNavigationButton(string text)
    {
        // Cria um botão padrão usado pelos links internos das páginas da janela.
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
        int top,
        int left,
        int width,
        int maximum = AppSettings.MaximumJoystickDeadzonePercent,
        bool valueBeforeSlider = false)
    {
        // Configura uma barra percentual; o rótulo pode ficar antes dela nas deadzones.
        slider.Minimum = 0;
        slider.Maximum = maximum;
        slider.Value = value;
        slider.TickFrequency = 5;
        slider.SmallChange = 1;
        slider.LargeChange = 5;
        slider.Bounds = new Rectangle(
            valueBeforeSlider ? left + 50 : left,
            top,
            valueBeforeSlider ? width - 50 : width,
            55);
        slider.AccessibleName = $"{accessibleName} percentage";
        parent.Controls.Add(slider);

        valueLabel.AutoSize = true;
        valueLabel.Text = $"{value}%";
        valueLabel.ForeColor = Color.White;
        valueLabel.Font = new Font("Segoe UI Semibold", 12F);
        valueLabel.Location = new Point(
            valueBeforeSlider ? left : left + width + 12,
            top + 12);
        parent.Controls.Add(valueLabel);
    }

    // Cria os controles de origem e destino e persiste alterações no remapeamento.
    private void AddButtonMappingRow(
        Control parent,
        PlayStationButton source,
        int column,
        int row)
    {
        var columnLeft = column == 0 ? 32 : 350;
        var top = 104 + row * 35;
        var sourceDisplayName = ButtonRemapping.GetDisplayName(source);
        var label = new Label
        {
            AutoSize = false,
            Text = sourceDisplayName,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            Bounds = new Rectangle(columnLeft, top, 105, 26)
        };
        parent.Controls.Add(label);
        _buttonMappingLabels[source] = label;

        var comboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Bounds = new Rectangle(columnLeft + 108, top, 178, 26),
            AccessibleName = $"{sourceDisplayName} mapping"
        };
        // Primeiro item: estado vazio, que desativa a emissão deste botão físico.
        comboBox.Items.Add(ButtonRemapping.EmptyOption);
        // Segunda ação especial: alterna entre os quatro slots de configuração.
        comboBox.Items.Add(ButtonRemapping.ConfigMapOption);
        foreach (var target in ButtonRemapping.XboxButtons)
        {
            comboBox.Items.Add(target);
        }

        var currentTarget = _settings.ButtonMappings[source];
        comboBox.SelectedItem = _settings.ConfigMapMappings.Contains(source)
            ? ButtonRemapping.ConfigMapOption
            : currentTarget is null
            ? ButtonRemapping.EmptyOption
            : ButtonRemapping.XboxButtons.First(option => option.Value == currentTarget);
        comboBox.SelectedIndexChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is not XboxButtonOption selectedTarget)
            {
                return;
            }

            if (selectedTarget.IsConfigMap)
            {
                _settings.ButtonMappings[source] = null;
                _settings.ConfigMapMappings.Add(source);
                ControllerManager.SetButtonMapping(source, null);
                ControllerManager.SetConfigMapMappings(_settings.ConfigMapMappings);
            }
            else
            {
                _settings.ConfigMapMappings.Remove(source);
                _settings.ButtonMappings[source] = selectedTarget.Value;
                ControllerManager.SetButtonMapping(source, selectedTarget.Value);
                ControllerManager.SetConfigMapMappings(_settings.ConfigMapMappings);
            }
            SaveSettings();
        };
        parent.Controls.Add(comboBox);
        _buttonMappingComboBoxes[source] = comboBox;
    }

    private static void AddSettingRow(
        Control parent,
        string title,
        string subtitle,
        int top,
        ToggleSwitch toggle)
    {
        // Organiza título, descrição e interruptor de uma configuração principal.
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

    // Alterna todo o perfil ativo sem afetar o estado temporário da emulação Xbox.
    private void SwitchConfigurationSlot(int slot)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SwitchConfigurationSlot(slot)));
            return;
        }

        _updatingSettings = true;
        try
        {
            _settings.ActiveSlot = slot;
            ControllerManager.SetActiveConfigurationSlot(slot);
            ControllerManager.SetButtonMappings(_settings.ButtonMappings);
            ControllerManager.SetConfigMapMappings(_settings.ConfigMapMappings);

            ControllerManager.LeftJoystickDeadzonePercent = _settings.LeftJoystickDeadzonePercent;
            ControllerManager.RightJoystickDeadzonePercent = _settings.RightJoystickDeadzonePercent;
            ControllerManager.LeftJoystickAntiDeadzonePercent = _settings.LeftJoystickAntiDeadzonePercent;
            ControllerManager.RightJoystickAntiDeadzonePercent = _settings.RightJoystickAntiDeadzonePercent;
            ControllerManager.LeftTriggerDeadzonePercent = _settings.LeftTriggerDeadzonePercent;
            ControllerManager.RightTriggerDeadzonePercent = _settings.RightTriggerDeadzonePercent;
            ControllerManager.LeftTriggerAntiDeadzonePercent = _settings.LeftTriggerAntiDeadzonePercent;
            ControllerManager.RightTriggerAntiDeadzonePercent = _settings.RightTriggerAntiDeadzonePercent;
            ControllerManager.SetAdaptiveTriggerIntensityPercent(_settings.AdaptiveTriggerIntensityPercent);
            ControllerManager.SetAdaptiveTriggerVibrationEnabled(_settings.AdaptiveTriggerVibrationEnabled);

            if (_settings.LightbarColor is Color lightbarColor)
            {
                ControllerManager.SetLightbarColor(lightbarColor);
            }
            else
            {
                ControllerManager.ClearLightbarColor();
            }

            _configurationSlotComboBox!.SelectedIndex = slot - 1;
            UpdateLightbarColorDisplay();
            _triggerVibrationToggle.Checked = _settings.AdaptiveTriggerVibrationEnabled;
            _triggerIntensitySlider.Value = _settings.AdaptiveTriggerIntensityPercent;
            _leftDeadzoneSlider.Value = _settings.LeftJoystickDeadzonePercent;
            _rightDeadzoneSlider.Value = _settings.RightJoystickDeadzonePercent;
            _leftAntiDeadzoneSlider.Value = _settings.LeftJoystickAntiDeadzonePercent;
            _rightAntiDeadzoneSlider.Value = _settings.RightJoystickAntiDeadzonePercent;
            _leftTriggerDeadzoneSlider.Value = _settings.LeftTriggerDeadzonePercent;
            _rightTriggerDeadzoneSlider.Value = _settings.RightTriggerDeadzonePercent;
            _leftTriggerAntiDeadzoneSlider.Value = _settings.LeftTriggerAntiDeadzonePercent;
            _rightTriggerAntiDeadzoneSlider.Value = _settings.RightTriggerAntiDeadzonePercent;

            foreach (var pair in _buttonMappingComboBoxes)
            {
                var source = pair.Key;
                var comboBox = pair.Value;
                var target = _settings.ButtonMappings[source];
                comboBox.SelectedItem = _settings.ConfigMapMappings.Contains(source)
                    ? ButtonRemapping.ConfigMapOption
                    : target is null
                        ? ButtonRemapping.EmptyOption
                        : ButtonRemapping.XboxButtons.First(option => option.Value == target);
            }
        }
        finally
        {
            _updatingSettings = false;
        }

        SaveSettings();
    }

    // Atualiza a amostra e o código RGB da cor definida no perfil ativo.
    private void UpdateLightbarColorDisplay()
    {
        var color = _settings.LightbarColor;
        _lightbarColorPreview!.BackColor = color ?? Color.FromArgb(75, 88, 108);
        _lightbarColorValue!.Text = color is Color selectedColor
            ? $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}"
            : "Not set";
    }

    private void ControllerToggle_CheckedChanged(object? sender, EventArgs e)
    {
        // Persiste a preferência e inicia ou encerra a tarefa de emulação.
        _settings.ControllerEmulationEnabled = _controllerToggle.Checked;
        _controllerSelectionButton.Enabled = false;
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

    // Inicia em segundo plano a ocultação do controle, a emulação e a reconexão.
    private void StartController()
    {
        // Cancela configurações antigas e inicia em segundo plano todo o encadeamento HID.
        SetTriggerVibrationToggle(false, false);
        _activationCancellation = new CancellationTokenSource();
        var cancellationToken = _activationCancellation.Token;
        var savedSelectionId = _settings.SelectedControllerInstanceId;
        SetStatus("Waiting for a compatible controller…", false);

        _activationTask = Task.Run(() =>
        {
            try
            {
                var selectedController = WaitForControllerSelection(
                    cancellationToken,
                    savedSelectionId);
                // XInput usa polling próprio e oculta suas interfaces HID encontradas.
                if (selectedController.Kind == ControllerConnectionKind.XInput)
                {
                    if (!XInputControllerManager.TryParseSelectionId(
                        selectedController.InstanceId,
                        out var xInputUserIndex))
                    {
                        throw new InvalidDataException("The selected XInput controller identifier is invalid.");
                    }

                    ControllerManager.SetXInputSourceActive(true, xInputUserIndex);
                    CloakManager.HideXInputControllers(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    XboxEmulator.Initialize();

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        XInputControllerManager.StartListening(
                            cancellationToken,
                            xInputUserIndex,
                            SetXInputController);

                        if (!cancellationToken.IsCancellationRequested)
                        {
                            SetStatus(
                                $"XInput controller {xInputUserIndex + 1} disconnected · waiting to reconnect",
                                false);
                            cancellationToken.WaitHandle.WaitOne(1000);
                        }
                    }
                }
                else
                {
                    // PlayStation HID usa leitura de relatórios e seleção pelo Device Instance ID.
                    ControllerManager.SetXInputSourceActive(false);
                    CloakManager.HideController(cancellationToken, selectedController.InstanceId);
                    cancellationToken.ThrowIfCancellationRequested();
                    XboxEmulator.Initialize();

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        ControllerManager.StartListening(
                            cancellationToken,
                            SetControllerType,
                            selectedController.InstanceId);

                        if (!cancellationToken.IsCancellationRequested)
                        {
                            SetStatus("Controller disconnected · waiting to reconnect", false);
                            cancellationToken.WaitHandle.WaitOne(1000);
                        }
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

    // Aguarda o dispositivo salvo reaparecer, ou escolhe o primeiro se não houver preferência.
    private ControllerSelectionOption WaitForControllerSelection(
        CancellationToken cancellationToken,
        string? savedSelectionId)
    {
        if (savedSelectionId?.StartsWith("XInput:", StringComparison.OrdinalIgnoreCase) == true
            && !XInputControllerManager.TryParseSelectionId(savedSelectionId, out _))
        {
            throw new InvalidDataException("The saved XInput controller selection is invalid.");
        }

        // A espera é cancelável para que desligar a emulação não prenda a interface.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connectedControllers = CloakManager.GetSupportedControllers();
            var selectedController = string.IsNullOrWhiteSpace(savedSelectionId)
                ? connectedControllers.FirstOrDefault()
                : connectedControllers.FirstOrDefault(controller =>
                    controller.InstanceId.Equals(
                        savedSelectionId,
                        StringComparison.OrdinalIgnoreCase));

            if (selectedController != null)
            {
                return selectedController;
            }

            SetStatus(
                string.IsNullOrWhiteSpace(savedSelectionId)
                    ? "Waiting for a compatible controller…"
                    : "Waiting for the selected controller…",
                false);
            cancellationToken.WaitHandle.WaitOne(1000);
        }
    }

    private async Task StopControllerAsync()
    {
        // Solicita cancelamento, aguarda a tarefa terminar e restaura o sistema físico.
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
        _controllerSelectionButton.Enabled = true;
    }

    // Atualiza a lista de dispositivos e restaura a seleção persistida quando possível.
    private void RefreshControllerSelectionList()
    {
        _controllerSelectionList.BeginUpdate();
        _controllerSelectionList.Items.Clear();
        _controllerSelectionApplyButton.Enabled = false;

        try
        {
            var controllers = CloakManager.GetSupportedControllers();
            foreach (var controller in controllers)
            {
                _controllerSelectionList.Items.Add(controller);
            }

            var selectedController = controllers.FirstOrDefault(controller =>
                controller.InstanceId.Equals(
                    _settings.SelectedControllerInstanceId,
                    StringComparison.OrdinalIgnoreCase));
            if (selectedController != null)
            {
                _controllerSelectionList.SelectedItem = selectedController;
            }
            else if (string.IsNullOrWhiteSpace(_settings.SelectedControllerInstanceId)
                && controllers.Count > 0)
            {
                _controllerSelectionList.SelectedIndex = 0;
            }

            _controllerSelectionStatus.Text = controllers.Count == 0
                ? "No compatible controllers are connected."
                : selectedController == null
                    && !string.IsNullOrWhiteSpace(_settings.SelectedControllerInstanceId)
                        ? "The saved controller is not connected. Select a connected controller."
                        : "Select the controller to use and hide.";
            _controllerSelectionApplyButton.Enabled =
                _controllerSelectionList.SelectedItem is ControllerSelectionOption;
        }
        catch (Exception ex) when (
            ex is IOException
                or InvalidOperationException
                or NotSupportedException
                or UnauthorizedAccessException
                or DllNotFoundException
                or BadImageFormatException
                or System.ComponentModel.Win32Exception)
        {
            Console.WriteLine($"Unable to enumerate compatible controllers: {ex.Message}");
            CrashReporter.Report("Failed to enumerate compatible controllers", ex);
            _controllerSelectionStatus.Text = "Unable to list controllers. Refresh to try again.";
        }
        finally
        {
            _controllerSelectionList.EndUpdate();
        }
    }

    private void SetControllerType(bool isDualSense)
    {
        // Atualiza o tipo detectado e habilita recursos exclusivos do DualSense.
        if (InvokeRequired)
        {
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(new Action(() => SetControllerType(isDualSense)));
            }

            return;
        }

        UpdateInputSourceAvailability(isXInput: false);
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

    // Atualiza o estado visual quando um slot XInput começa a fornecer entrada.
    private void SetXInputController(int userIndex)
    {
        if (InvokeRequired)
        {
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(new Action(() => SetXInputController(userIndex)));
            }

            return;
        }

        UpdateInputSourceAvailability(isXInput: true);
        SetTriggerVibrationToggle(false, false);
        SetStatus(
            $"XInput controller {userIndex + 1} connected · Xbox 360 emulation active",
            true);
    }

    // Habilita somente as opções que fazem sentido para a origem atualmente selecionada.
    private void UpdateInputSourceAvailability(bool isXInput)
    {
        if (_adaptiveTriggerSettingsButton != null)
        {
            _adaptiveTriggerSettingsButton.Enabled = !isXInput;
        }

        if (_lightbarColorHeading != null)
        {
            _lightbarColorHeading.Visible = !isXInput;
        }

        if (_lightbarColorButton != null)
        {
            _lightbarColorButton.Visible = !isXInput;
        }

        if (_lightbarColorPreview != null)
        {
            _lightbarColorPreview.Visible = !isXInput;
        }

        if (_lightbarColorValue != null)
        {
            _lightbarColorValue.Visible = !isXInput;
        }

        if (_buttonMappingDescription != null)
        {
            _buttonMappingDescription.Text = isXInput
                ? "Choose the Xbox 360 output for each XInput button."
                : "Choose the Xbox 360 button emitted for each PlayStation digital button.";
        }

        foreach (var (source, label) in _buttonMappingLabels)
        {
            var displayName = ButtonRemapping.GetDisplayName(source, isXInput);
            label.Text = displayName;
            _buttonMappingComboBoxes[source].AccessibleName = $"{displayName} mapping";
            _buttonMappingComboBoxes[source].Enabled =
                !isXInput || source != PlayStationButton.Touchpad;
        }
    }

    private void SetTriggerVibrationToggle(bool isChecked, bool isEnabled)
    {
        // Atualiza o interruptor sem disparar novamente o evento de persistência.
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

    private bool SaveSettings()
    {
        // Persiste os valores atuais e apresenta falhas de acesso ao arquivo.
        try
        {
            _settings.Save();
            _settingsSaveErrorShown = false;
            return true;
        }
        catch (IOException ex)
        {
            ReportSettingsSaveFailure(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            ReportSettingsSaveFailure(ex);
        }

        return false;
    }

    private void ReportSettingsSaveFailure(Exception exception)
    {
        // Registra a falha e limita a notificação visual a uma mensagem por sessão.
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
        // Restaura visibilidade, encerra streams HID e desmonta o emulador Xbox.
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
            try
            {
                XboxEmulator.Shutdown();
            }
            finally
            {
                ControllerManager.SetXInputSourceActive(false);
            }
        }
    }

    private void SetStatus(string message, bool active)
    {
        // Atualiza os elementos visuais de estado pela thread correta da interface.
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

    // Cancela tarefas pendentes e encerra os dispositivos antes de fechar a janela.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Interrompe tarefas, libera streams e tenta limpar o controlador antes de fechar.
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
    // Estado atual do interruptor apresentado pela interface.
    private bool _checked;

    // Evento disparado sempre que o estado Checked muda.
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
        // Habilita pintura dupla, redesenho e comportamento adequado como botão de seleção.
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
        // Alterna o estado ao clicar, mas respeita o estado Enabled.
        base.OnClick(e);
        if (Enabled)
        {
            Checked = !Checked;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Permite usar barra de espaço para ligar ou desligar o controle.
        base.OnKeyDown(e);
        if (Enabled && e.KeyCode == Keys.Space)
        {
            Checked = !Checked;
            e.Handled = true;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Desenha trilha arredondada, indicador de estado e botão circular.
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
