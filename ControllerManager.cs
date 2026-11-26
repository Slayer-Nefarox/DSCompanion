using HidSharp;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using DualSenseCompanion;
using System.Buffers.Binary;
using System.Drawing;
using System.Reactive.Linq;
using System.Threading;

// Conecta aos controles HID PlayStation, interpreta relatórios e envia estados ao Xbox virtual.
class ControllerManager
{
    // Identifica qual protocolo específico de entrada e saída deve ser utilizado.
    private enum ControllerType
    {
        DualSense,
        DualShock4
    }

    // Dispositivo físico selecionado no sistema.
    private static HidDevice? _controller;
    // Fluxo responsável pela leitura contínua dos estados do controle.
    private static HidStream? _inputStream;
    // Fluxo responsável pelo envio de vibração e efeitos ao controle.
    private static HidStream? _outputStream;
    // Impede escritas simultâneas no fluxo de saída.
    private static readonly object _vibrationWriteLock = new();
    // Indica se o controle está conectado por Bluetooth.
    private static volatile bool _isBluetooth;
    private static volatile bool _isXInputSource;
    private static int _xInputUserIndex = -1;
    // Tipo detectado: DualSense ou DualShock 4.
    private static ControllerType _controllerType;
    // Tamanho máximo suportado pelo relatório de saída do dispositivo.
    private static int _outputReportLength;
    // Percentuais aplicados aos limites internos e externos dos analógicos.
    private static int _leftJoystickDeadzonePercent;
    private static int _rightJoystickDeadzonePercent;
    private static int _leftJoystickAntiDeadzonePercent;
    private static int _rightJoystickAntiDeadzonePercent;
    // Percentuais aplicados às zonas mortas e aos limites externos de L2 e R2.
    private static int _leftTriggerDeadzonePercent;
    private static int _rightTriggerDeadzonePercent;
    private static int _leftTriggerAntiDeadzonePercent;
    private static int _rightTriggerAntiDeadzonePercent;
    private static int _lightbarColorArgb = Color.White.ToArgb();
    private static int _hasLightbarColor;
    private static Xbox360Button?[] _buttonMappings = ButtonRemapping.CreateDefaultTargetArray();
    // Fontes físicas cujo acionamento alterna os perfis de configuração.
    private static readonly HashSet<PlayStationButton> _configMapMappings = new();
    // Estado anterior das fontes usado para disparar Config map apenas na borda de pressão.
    private static readonly bool[] _previousButtonStates = new bool[ButtonRemapping.PlayStationButtons.Count];
    // Slot atualmente ativo e limite de perfis suportados.
    private static int _activeConfigurationSlot = 1;
    private const int ConfigurationSlotCount = 4;
    // Função chamada pela UI ao trocar de perfil.
    private static Action<int>? _configurationSlotChanged;
    private static volatile bool _adaptiveTriggerVibrationEnabled;
    // Intensidade configurada para os gatilhos; zero acompanha o motor mais forte.
    private static int _adaptiveTriggerIntensityPercent;
    private static byte _lastLeftMotor;
    private static byte _lastRightMotor;
    private static byte _bluetoothSequence;
    private static int _dualSenseBluetoothLedsReleased;
    private static long _dualSenseBluetoothLedReleaseDeadline;
    private const uint DualSenseBluetoothConnectionCompleteTimestamp = 10_200_000;
    private static int _configurationPlayerLed = 1;
    private static int _dualSenseWriteFailureReported;
    private static int _dualShock4WriteFailureReported;

    // Zona neutra do analógico esquerdo, publicada com leitura segura entre threads.
    public static int LeftJoystickDeadzonePercent
    {
        get => Volatile.Read(ref _leftJoystickDeadzonePercent);
        set
        {
            Volatile.Write(ref _leftJoystickDeadzonePercent, ValidateJoystickDeadzone(value));
        }
    }

    // Zona neutra do analógico direito, publicada com leitura segura entre threads.
    public static int RightJoystickDeadzonePercent
    {
        get => Volatile.Read(ref _rightJoystickDeadzonePercent);
        set
        {
            Volatile.Write(ref _rightJoystickDeadzonePercent, ValidateJoystickDeadzone(value));
        }
    }

    // Define a compensação do limite externo do analógico esquerdo.
    public static int LeftJoystickAntiDeadzonePercent
    {
        get => Volatile.Read(ref _leftJoystickAntiDeadzonePercent);
        set => Volatile.Write(ref _leftJoystickAntiDeadzonePercent, ValidateJoystickDeadzone(value));
    }

    // Define a compensação do limite externo do analógico direito.
    public static int RightJoystickAntiDeadzonePercent
    {
        get => Volatile.Read(ref _rightJoystickAntiDeadzonePercent);
        set => Volatile.Write(ref _rightJoystickAntiDeadzonePercent, ValidateJoystickDeadzone(value));
    }

    // Zona neutra do gatilho esquerdo, publicada com leitura segura entre threads.
    public static int LeftTriggerDeadzonePercent
    {
        get => Volatile.Read(ref _leftTriggerDeadzonePercent);
        set => Volatile.Write(ref _leftTriggerDeadzonePercent, ValidateTriggerDeadzone(value));
    }

    // Zona neutra do gatilho direito, publicada com leitura segura entre threads.
    public static int RightTriggerDeadzonePercent
    {
        get => Volatile.Read(ref _rightTriggerDeadzonePercent);
        set => Volatile.Write(ref _rightTriggerDeadzonePercent, ValidateTriggerDeadzone(value));
    }

    // Compensação externa do gatilho esquerdo, publicada com leitura segura entre threads.
    public static int LeftTriggerAntiDeadzonePercent
    {
        get => Volatile.Read(ref _leftTriggerAntiDeadzonePercent);
        set => Volatile.Write(ref _leftTriggerAntiDeadzonePercent, ValidateTriggerDeadzone(value));
    }

    // Compensação externa do gatilho direito, publicada com leitura segura entre threads.
    public static int RightTriggerAntiDeadzonePercent
    {
        get => Volatile.Read(ref _rightTriggerAntiDeadzonePercent);
        set => Volatile.Write(ref _rightTriggerAntiDeadzonePercent, ValidateTriggerDeadzone(value));
    }

    // Valida deadzones e anti-deadzones dos gatilhos no intervalo aceito pela interface.
    private static int ValidateTriggerDeadzone(int value)
    {
        // Mantém os quatro ajustes dos gatilhos dentro da faixa configurável.
        if (value < 0 || value > AppSettings.MaximumTriggerDeadzonePercent)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return value;
    }

    // Valida deadzones e anti-deadzones dos analógicos antes de publicá-las.
    private static int ValidateJoystickDeadzone(int value)
    {
        // Rejeita valores fora do intervalo aceito pelas propriedades de configuração.
        if (value < 0 || value > AppSettings.MaximumJoystickDeadzonePercent)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return value;
    }

    // Substitui atomicamente todas as associações físicas para as associações Xbox.
    public static void SetButtonMappings(
        IReadOnlyDictionary<PlayStationButton, Xbox360Button?> mappings)
    {
        // Constrói uma cópia completa do vetor para publicar todas as associações atomicamente.
        var updatedMappings = new Xbox360Button?[ButtonRemapping.PlayStationButtons.Count];
        foreach (var source in ButtonRemapping.PlayStationButtons)
        {
            if (!mappings.TryGetValue(source, out var target)
                || (target is Xbox360Button button && !ButtonRemapping.IsXboxButton(button)))
            {
                throw new ArgumentException($"Missing or invalid mapping for {source}.", nameof(mappings));
            }

            updatedMappings[(int)source] = target;
        }

        Volatile.Write(ref _buttonMappings, updatedMappings);
    }

    // Substitui uma única associação física sem bloquear o processo de leitura.
    public static void SetButtonMapping(PlayStationButton source, Xbox360Button? target)
    {
        // Valida o botão de origem e cria uma nova cópia antes de alterar um único item.
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        if (target is Xbox360Button button && !ButtonRemapping.IsXboxButton(button))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        var updatedMappings = (Xbox360Button?[])Volatile.Read(ref _buttonMappings).Clone();
        updatedMappings[(int)source] = target;
        Volatile.Write(ref _buttonMappings, updatedMappings);
    }

    // Define todas as fontes que executam a ação especial Config map.
    public static void SetConfigMapMappings(IReadOnlySet<PlayStationButton> sources)
    {
        lock (_configMapMappings)
        {
            _configMapMappings.Clear();
            foreach (var source in sources)
            {
                if (Enum.IsDefined(source))
                {
                    _configMapMappings.Add(source);
                }
            }
        }
    }

    // Ativa a rota XInput ou encerra a vibração do slot ao retornar para HID.
    public static void SetXInputSourceActive(bool active, int userIndex = -1)
    {
        // Um índice válido é obrigatório apenas ao ativar uma origem XInput.
        if (active && (userIndex < 0 || userIndex >= 4))
        {
            throw new ArgumentOutOfRangeException(nameof(userIndex));
        }

        if (active)
        {
            Volatile.Write(ref _xInputUserIndex, userIndex);
            _isXInputSource = true;
        }
        else
        {
            // Silencia e limpa o slot anterior antes de descartar sua identificação.
            var previousUserIndex = Volatile.Read(ref _xInputUserIndex);
            if (_isXInputSource && previousUserIndex >= 0)
            {
                XInputControllerManager.SetVibration(previousUserIndex, 0, 0);
            }

            _isXInputSource = false;
            Volatile.Write(ref _xInputUserIndex, -1);
            _lastLeftMotor = 0;
            _lastRightMotor = 0;
        }

        Array.Clear(_previousButtonStates);
    }

    // Registra o callback usado pela interface para aplicar e persistir o novo slot.
    public static void SetConfigurationSlotHandler(Action<int> handler)
    {
        _configurationSlotChanged = handler;
    }

    // Define o slot ativo sem disparar o callback da própria alteração.
    public static void SetActiveConfigurationSlot(int slot)
    {
        _activeConfigurationSlot = Math.Clamp(slot, 1, ConfigurationSlotCount);
        _configurationPlayerLed = _activeConfigurationSlot;
        if (_outputStream != null && _controllerType == ControllerType.DualSense)
        {
            SendDualSenseOutput(_lastLeftMotor, _lastRightMotor, _adaptiveTriggerVibrationEnabled);
        }
    }

    // Alterna na borda de pressão e informa a interface para aplicar todas as opções do perfil.
    private static void ApplyConfigMapEdge(PlayStationButton source, bool isPressed)
    {
        var index = (int)source;
        var wasPressed = _previousButtonStates[index];
        _previousButtonStates[index] = isPressed;

        var isConfigMapSource = false;
        lock (_configMapMappings)
        {
            isConfigMapSource = _configMapMappings.Contains(source);
        }

        if (isPressed && !wasPressed && isConfigMapSource)
        {
            var nextSlot = _activeConfigurationSlot >= ConfigurationSlotCount
                ? 1
                : _activeConfigurationSlot + 1;
            _activeConfigurationSlot = nextSlot;
            _configurationPlayerLed = nextSlot;
            _configurationSlotChanged?.Invoke(nextSlot);
            if (_outputStream != null && _controllerType == ControllerType.DualSense)
            {
                SendDualSenseOutput(_lastLeftMotor, _lastRightMotor, _adaptiveTriggerVibrationEnabled);
            }
        }
    }

    // Aplica uma nova cor RGB à barra de luz e reenvia o estado atual ao hardware.
    public static void SetLightbarColor(Color color)
    {
        // Remove canal alfa e mantém somente vermelho, verde e azul.
        var opaqueColor = Color.FromArgb(color.R, color.G, color.B);
        Interlocked.Exchange(ref _lightbarColorArgb, opaqueColor.ToArgb());
        Volatile.Write(ref _hasLightbarColor, 1);

        SendCurrentLightbarOutput();
    }

    // Restaura a saída de luz padrão quando o perfil ativo não define uma cor.
    public static void ClearLightbarColor()
    {
        Volatile.Write(ref _hasLightbarColor, 0);
        SendCurrentLightbarOutput();
    }

    // Reenvia a cor efetiva usando o formato de saída do modelo conectado.
    private static void SendCurrentLightbarOutput()
    {
        if (_outputStream == null)
        {
            return;
        }

        if (_controllerType == ControllerType.DualShock4)
        {
            SendVibrationToDualShock4(_lastLeftMotor, _lastRightMotor);
        }
        else
        {
            SendDualSenseOutput(_lastLeftMotor, _lastRightMotor, _adaptiveTriggerVibrationEnabled);
        }
    }

    // Abre os fluxos HID e processa os relatórios até haver cancelamento ou desconexão.
    public static void StartListening(
        CancellationToken cancellationToken,
        Action<bool> controllerDetected,
        string? selectedInstanceId = null)
    {
        // Interrompe antes de iniciar caso o cancelamento já tenha sido solicitado.
        cancellationToken.ThrowIfCancellationRequested();
        // Abre o mesmo dispositivo selecionado para ocultação no HidHide.
        _controller = GetController(selectedInstanceId);

        if (_controller == null)
        {
            Console.WriteLine("Supported PlayStation controller not found. Exiting...");
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _controllerType = _controller.ProductID == 0x0CE6
            ? ControllerType.DualSense
            : ControllerType.DualShock4;
        string controllerName = _controllerType == ControllerType.DualSense ? "DualSense" : "DualShock 4";
        int inputReportLength = 0;
        try
        {
            _isBluetooth = _controller.GetMaxInputReportLength() > 64;
            inputReportLength = _controller.GetMaxInputReportLength();
            _outputReportLength = _controller.GetMaxOutputReportLength();
            Console.WriteLine($"{controllerName} detected ({(_isBluetooth ? "Bluetooth" : "USB")})");
            Console.WriteLine($"HID report lengths: input={inputReportLength}, output={_outputReportLength}");
            Console.WriteLine("Starting input listener...");

            _inputStream = _controller.Open();
            _inputStream.ReadTimeout = Timeout.Infinite;
            _outputStream = _controller.Open();
            // Reinicia a indicação de falhas de escrita em cada nova conexão.
            Interlocked.Exchange(ref _dualSenseWriteFailureReported, 0);
            Interlocked.Exchange(ref _dualShock4WriteFailureReported, 0);
            if (_controllerType == ControllerType.DualSense && _isBluetooth)
            {
                InitializeDualSenseBluetooth();
            }

            controllerDetected(_controllerType == ControllerType.DualSense);

            byte[] inputReport = new byte[Math.Max(inputReportLength, 78)];

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Cada leitura contém o estado atual dos sticks, gatilhos e botões.
                    int bytesRead = _inputStream.Read(inputReport);
                    if (bytesRead > 0)
                    {
                        ProcessInput(inputReport, bytesRead);
                    }
                }
                catch (IOException ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        Console.WriteLine("Controller disconnected.");
                        Console.WriteLine($"Error: {ex.Message}");
                        CrashReporter.Report(
                            $"Controller input stream disconnected unexpectedly (type={controllerName}, bluetooth={_isBluetooth}, inputReportLength={inputReport.Length}, outputReportLength={_outputReportLength})",
                            ex);
                    }

                    break;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        catch (IOException ex) when (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"Controller connection failed: {ex.Message}");
            CrashReporter.Report(
                $"Failed to open controller HID streams (type={controllerName}, bluetooth={_isBluetooth}, inputReportLength={inputReportLength}, outputReportLength={_outputReportLength})",
                ex);
        }
        finally
        {
            _inputStream?.Dispose();
            _outputStream?.Dispose();
            _inputStream = null;
            _outputStream = null;
        }
    }

    // Fecha os fluxos de entrada e saída para interromper a leitura HID em espera.
    public static void StopListening()
    {
        // Fecha os fluxos HID; isso também faz a operação Read em espera retornar.
        _inputStream?.Dispose();
        _outputStream?.Dispose();
    }

    // Encaminha cada relatório ao decodificador adequado para o modelo conectado.
    private static void ProcessInput(byte[] report, int reportLength)
    {
        // Encaminha a interpretação conforme o modelo detectado e o tipo do relatório.
        if (_controllerType == ControllerType.DualShock4)
        {
            ProcessDualShock4Input(report, reportLength);
            return;
        }

        if (reportLength < 10)
        {
            Console.WriteLine("Invalid report length.");
            return;
        }

        byte reportId = report[0];

        if (reportId == 0x01)
        {
            if (reportLength == 10)
            {
                ConfirmBluetoothTransport();
            }

            TryReleaseDualSenseBluetoothLeds(report, reportLength);
            ProcessStandardInput(report);
        }
        else if (reportId == 0x31)
        {
            if (reportLength < 12)
            {
                Console.WriteLine("Invalid DualSense Bluetooth report length.");
                return;
            }

            ConfirmBluetoothTransport();
            ProcessExtendedInput(report, reportLength);
        }
        else
        {
            Console.WriteLine($"Unknown report type");
        }
    }

    private static void ProcessStandardInput(byte[] report)
    {   
        // Método de decodificação usado pelos relatórios padrão USB/Bluetooth.
        // Diferentes posições de bytes são usadas conforme a conexão USB ou Bluetooth.
        int dpadOffset = _isBluetooth ? 5 : 8;
        int buttonOffset = _isBluetooth ? 5 : 8;
        int shoulderOffset = _isBluetooth ? 6 : 9;
        int specialButtonOffset = _isBluetooth ? 7 : 10;
        int triggerOffset = _isBluetooth ? 8 : 5;

        // Lê os quatro eixos brutos dos dois analógicos.
        byte leftStickX = report[1];
        byte leftStickY = report[2];
        byte rightStickX = report[3];
        byte rightStickY = report[4];

        // Converte o intervalo 0–255 para o intervalo com sinal -32768–32767.
        short xboxLeftStickX = (short)((leftStickX - 128) * 258);
        short xboxLeftStickY = (short)((127 - leftStickY) * 258);
        short xboxRightStickX = (short)((rightStickX - 128) * 258);
        short xboxRightStickY = (short)((127 - rightStickY) * 258);

        // Preserva os limites absolutos para evitar inversões nos cantos dos analógicos.
        if (leftStickX == 0) xboxLeftStickX = -32768;
        if (leftStickX == 255) xboxLeftStickX = 32767;
        if (leftStickY == 0) xboxLeftStickY = 32767;
        if (leftStickY == 255) xboxLeftStickY = -32768;

        if (rightStickX == 0) xboxRightStickX = -32768;
        if (rightStickX == 255) xboxRightStickX = 32767;
        if (rightStickY == 0) xboxRightStickY = 32767;
        if (rightStickY == 255) xboxRightStickY = -32768;

        // Extrai a pressão dos dois gatilhos, que também usa faixa 0–255.
        byte lTrigger = report[triggerOffset];
        byte rTrigger = report[triggerOffset + 1];

        // Decodifica o D-pad por meio do valor de quatro bits do relatório HID.
        byte dpadState = (byte)(report[dpadOffset] & 0x0F);
        bool dpadUp = dpadState == 0 || dpadState == 1 || dpadState == 7;
        bool dpadRight = dpadState == 1 || dpadState == 2 || dpadState == 3;
        bool dpadDown = dpadState == 3 || dpadState == 4 || dpadState == 5;
        bool dpadLeft = dpadState == 5 || dpadState == 6 || dpadState == 7;

        // Converte máscaras de bits em estados individuais dos botões.
        bool squarePressed = (report[buttonOffset] & 0x10) != 0;
        bool crossPressed = (report[buttonOffset] & 0x20) != 0;
        bool circlePressed = (report[buttonOffset] & 0x40) != 0;
        bool trianglePressed = (report[buttonOffset] & 0x80) != 0;

        bool l1Pressed = (report[shoulderOffset] & 0x01) != 0;
        bool r1Pressed = (report[shoulderOffset] & 0x02) != 0;
        bool l3Pressed = (report[shoulderOffset] & 0x40) != 0;
        bool r3Pressed = (report[shoulderOffset] & 0x80) != 0;
        bool createPressed = (report[shoulderOffset] & 0x10) != 0;
        bool optionsPressed = (report[shoulderOffset] & 0x20) != 0;
        bool psPressed = (report[specialButtonOffset] & 0x01) != 0;
        // O clique do touchpad usa o bit 0x02 no byte especial de cada modelo.
        bool touchpadPressed = (report[specialButtonOffset] & 0x02) != 0;

        MapToXboxController(dpadUp, dpadDown, dpadLeft, dpadRight, squarePressed, crossPressed, circlePressed, trianglePressed,
                            l1Pressed, r1Pressed, l3Pressed, r3Pressed, createPressed, optionsPressed, psPressed, touchpadPressed,
                            lTrigger, rTrigger, xboxLeftStickX, xboxLeftStickY, xboxRightStickX, xboxRightStickY);
    }

    // Converte as máscaras e eixos XInput para a mesma entrada lógica PlayStation.
    public static void ProcessXInputInput(XInputGamepadState state)
    {
        // Máscaras definidas pela API XInput para os botões do gamepad.
        const ushort dpadUpMask = 0x0001;
        const ushort dpadDownMask = 0x0002;
        const ushort dpadLeftMask = 0x0004;
        const ushort dpadRightMask = 0x0008;
        const ushort startMask = 0x0010;
        const ushort backMask = 0x0020;
        const ushort leftThumbMask = 0x0040;
        const ushort rightThumbMask = 0x0080;
        const ushort leftShoulderMask = 0x0100;
        const ushort rightShoulderMask = 0x0200;
        const ushort guideMask = 0x0400;
        const ushort aMask = 0x1000;
        const ushort bMask = 0x2000;
        const ushort xMask = 0x4000;
        const ushort yMask = 0x8000;

        var buttons = state.Buttons;
        MapToXboxController(
            (buttons & dpadUpMask) != 0,
            (buttons & dpadDownMask) != 0,
            (buttons & dpadLeftMask) != 0,
            (buttons & dpadRightMask) != 0,
            (buttons & xMask) != 0,
            (buttons & aMask) != 0,
            (buttons & bMask) != 0,
            (buttons & yMask) != 0,
            (buttons & leftShoulderMask) != 0,
            (buttons & rightShoulderMask) != 0,
            (buttons & leftThumbMask) != 0,
            (buttons & rightThumbMask) != 0,
            (buttons & backMask) != 0,
            (buttons & startMask) != 0,
            (buttons & guideMask) != 0,
            touchpadPressed: false,
            state.LeftTrigger,
            state.RightTrigger,
            state.LeftThumbX,
            state.LeftThumbY,
            state.RightThumbX,
            state.RightThumbY);
    }

    private static void ProcessDualShock4Input(byte[] report, int reportLength)
    {
        // Decodifica os formatos USB e Bluetooth específicos do DualShock 4.
        // Seleciona os deslocamentos corretos para cada formato USB ou Bluetooth.
        int stickOffset;
        int triggerOffset;
        int buttonOffset;
        int shoulderOffset;
        int specialButtonOffset;
        bool isBluetoothReport = false;

        if (report[0] == 0x01)
        {
            stickOffset = 1;
            triggerOffset = 8;
            buttonOffset = 5;
            shoulderOffset = 6;
            specialButtonOffset = 7;
            isBluetoothReport = reportLength == 10;
        }
        else if (report[0] == 0x11)
        {
            if (reportLength < 11)
            {
                Console.WriteLine("Invalid DualShock 4 Bluetooth report length.");
                return;
            }

            stickOffset = 2;
            triggerOffset = 6;
            buttonOffset = 8;
            shoulderOffset = 9;
            specialButtonOffset = 10;
            isBluetoothReport = true;
        }
        else
        {
            Console.WriteLine($"Unknown DualShock 4 report type: 0x{report[0]:X2}");
            return;
        }

        int requiredLength = Math.Max(specialButtonOffset, triggerOffset + 1) + 1;
        if (reportLength < requiredLength)
        {
            Console.WriteLine("Invalid DualShock 4 report length.");
            return;
        }

        if (isBluetoothReport)
        {
            ConfirmBluetoothTransport();
        }

        byte leftStickX = report[stickOffset];
        byte leftStickY = report[stickOffset + 1];
        byte rightStickX = report[stickOffset + 2];
        byte rightStickY = report[stickOffset + 3];

        short xboxLeftStickX = ConvertStickX(leftStickX);
        short xboxLeftStickY = ConvertStickY(leftStickY);
        short xboxRightStickX = ConvertStickX(rightStickX);
        short xboxRightStickY = ConvertStickY(rightStickY);

        byte dpadState = (byte)(report[buttonOffset] & 0x0F);
        bool dpadUp = dpadState == 0 || dpadState == 1 || dpadState == 7;
        bool dpadRight = dpadState == 1 || dpadState == 2 || dpadState == 3;
        bool dpadDown = dpadState == 3 || dpadState == 4 || dpadState == 5;
        bool dpadLeft = dpadState == 5 || dpadState == 6 || dpadState == 7;

        bool squarePressed = (report[buttonOffset] & 0x10) != 0;
        bool crossPressed = (report[buttonOffset] & 0x20) != 0;
        bool circlePressed = (report[buttonOffset] & 0x40) != 0;
        bool trianglePressed = (report[buttonOffset] & 0x80) != 0;

        bool l1Pressed = (report[shoulderOffset] & 0x01) != 0;
        bool r1Pressed = (report[shoulderOffset] & 0x02) != 0;
        bool l3Pressed = (report[shoulderOffset] & 0x40) != 0;
        bool r3Pressed = (report[shoulderOffset] & 0x80) != 0;
        bool sharePressed = (report[shoulderOffset] & 0x10) != 0;
        bool optionsPressed = (report[shoulderOffset] & 0x20) != 0;
        bool psPressed = (report[specialButtonOffset] & 0x01) != 0;
        // DualShock 4: o clique do touchpad é o bit 0x02 no byte especial.
        bool touchpadPressed = (report[specialButtonOffset] & 0x02) != 0;

        MapToXboxController(dpadUp, dpadDown, dpadLeft, dpadRight, squarePressed, crossPressed, circlePressed, trianglePressed,
                            l1Pressed, r1Pressed, l3Pressed, r3Pressed, sharePressed, optionsPressed, psPressed, touchpadPressed,
                            report[triggerOffset], report[triggerOffset + 1],
                            xboxLeftStickX, xboxLeftStickY, xboxRightStickX, xboxRightStickY);
    }

    // Corrige o transporte detectado pelo relatório e reinicializa a saída Bluetooth.
    private static void ConfirmBluetoothTransport()
    {
        if (_isBluetooth)
        {
            return;
        }

        _isBluetooth = true;
        Console.WriteLine("Bluetooth input report detected; correcting the output transport.");
        if (_controllerType == ControllerType.DualSense)
        {
            InitializeDualSenseBluetooth();
        }

        SendVibrationToController(_lastLeftMotor, _lastRightMotor);
    }

    // Lê a calibração Bluetooth e agenda a liberação dos LEDs após a animação de conexão.
    private static void InitializeDualSenseBluetooth()
    {
        // Dá até seis segundos para observar o fim da animação no relatório de entrada.
        Volatile.Write(ref _dualSenseBluetoothLedsReleased, 0);
        Interlocked.Exchange(
            ref _dualSenseBluetoothLedReleaseDeadline,
            Environment.TickCount64 + 6_000);

        var calibrationReport = new byte[41];
        calibrationReport[0] = 0x05;

        try
        {
            _inputStream!.GetFeature(calibrationReport);
        }
        catch (Exception ex) when (
            ex is IOException or TimeoutException or NotSupportedException or InvalidOperationException)
        {
            Console.WriteLine($"Failed to read DualSense Bluetooth calibration report: {ex.Message}");
        }

    }

    // Libera o controle dos LEDs quando a animação inicial do DualSense termina ou expira.
    private static void TryReleaseDualSenseBluetoothLeds(byte[] inputReport, int reportLength)
    {
        if (_controllerType != ControllerType.DualSense
            || !_isBluetooth
            || Volatile.Read(ref _dualSenseBluetoothLedsReleased) != 0)
        {
            return;
        }

        // Usa o timestamp do sensor apenas quando o relatório Bluetooth e seu CRC são válidos.
        var animationComplete = false;
        if (reportLength >= 33 && inputReport[0] == 0x31 && HasValidDualSenseBluetoothInputCrc(inputReport, reportLength))
        {
            var sensorTimestamp = BinaryPrimitives.ReadUInt32LittleEndian(inputReport.AsSpan(29, sizeof(uint)));
            animationComplete = sensorTimestamp >= DualSenseBluetoothConnectionCompleteTimestamp;
        }

        if (!animationComplete
            && Environment.TickCount64 < Interlocked.Read(ref _dualSenseBluetoothLedReleaseDeadline))
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _dualSenseBluetoothLedsReleased, 1, 0) != 0)
        {
            return;
        }

        if (!SendDualSenseBluetoothLedRelease())
        {
            Volatile.Write(ref _dualSenseBluetoothLedsReleased, 0);
            return;
        }

        SendDualSenseOutput(_lastLeftMotor, _lastRightMotor, _adaptiveTriggerVibrationEnabled);
    }

    // Valida o CRC do relatório Bluetooth antes de confiar em seus campos estendidos.
    private static bool HasValidDualSenseBluetoothInputCrc(byte[] report, int reportLength)
    {
        if (reportLength != 78)
        {
            return false;
        }

        var crcData = new byte[75];
        crcData[0] = 0xA1;
        Array.Copy(report, 0, crcData, 1, 74);
        var expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(report.AsSpan(74, sizeof(uint)));
        return NewCrc.ComputeDualShock4BluetoothCRC32(crcData) == expectedCrc;
    }

    private static short ConvertStickX(byte value)
    {
        // Converte a coordenada horizontal bruta para o intervalo do eixo Xbox.
        return value switch
        {
            0 => short.MinValue,
            255 => short.MaxValue,
            _ => (short)((value - 128) * 258)
        };
    }

    private static short ConvertStickY(byte value)
    {
        // Converte a coordenada vertical invertendo o sentido usado pelo controlador.
        return value switch
        {
            0 => short.MaxValue,
            255 => short.MinValue,
            _ => (short)((127 - value) * 258)
        };
    }



    // Decodifica o relatório estendido usado pelo DualSense via Bluetooth.
    private static void ProcessExtendedInput(byte[] report, int reportLength)
    {
        TryReleaseDualSenseBluetoothLeds(report, reportLength);

        // Decodifica o relatório estendido usado pelo DualSense Bluetooth.
        // Este formato estendido é usado pelo DualSense via Bluetooth.
        // Lê os eixos dos dois analógicos em suas posições fixas.
        byte leftStickX = report[2];
        byte leftStickY = report[3];
        byte rightStickX = report[4];
        byte rightStickY = report[5];

        // Converte as coordenadas brutas para o padrão usado pelo Xbox virtual.
        short xboxLeftStickX = (short)((leftStickX - 128) * 258);
        short xboxLeftStickY = (short)((127 - leftStickY) * 258);
        short xboxRightStickX = (short)((rightStickX - 128) * 258);
        short xboxRightStickY = (short)((127 - rightStickY) * 258);

        // Corrige os valores extremos dos analógicos.
        if (leftStickX == 0) xboxLeftStickX = -32768;
        if (leftStickX == 255) xboxLeftStickX = 32767;
        if (leftStickY == 0) xboxLeftStickY = 32767;
        if (leftStickY == 255) xboxLeftStickY = -32768;

        if (rightStickX == 0) xboxRightStickX = -32768;
        if (rightStickX == 255) xboxRightStickX = 32767;
        if (rightStickY == 0) xboxRightStickY = 32767;
        if (rightStickY == 255) xboxRightStickY = -32768;

        // Extrai os gatilhos esquerdo e direito.
        byte lTrigger = report[6];
        byte rTrigger = report[7];

        // Decodifica as direções do D-pad.
        byte dpadState = (byte)(report[9] & 0x0F);
        bool dpadUp = dpadState == 0 || dpadState == 1 || dpadState == 7;
        bool dpadRight = dpadState == 1 || dpadState == 2 || dpadState == 3;
        bool dpadDown = dpadState == 3 || dpadState == 4 || dpadState == 5;
        bool dpadLeft = dpadState == 5 || dpadState == 6 || dpadState == 7;

        // Extrai os estados dos botões por meio de máscaras de bits.
        bool squarePressed = (report[9] & (1 << 4)) != 0;
        bool crossPressed = (report[9] & (1 << 5)) != 0;
        bool circlePressed = (report[9] & (1 << 6)) != 0;
        bool trianglePressed = (report[9] & (1 << 7)) != 0;

        bool l1Pressed = (report[10] & (1 << 0)) != 0;
        bool r1Pressed = (report[10] & (1 << 1)) != 0;
        bool l3Pressed = (report[10] & (1 << 6)) != 0;
        bool r3Pressed = (report[10] & (1 << 7)) != 0;
        bool createPressed = (report[10] & (1 << 4)) != 0;
        bool optionsPressed = (report[10] & (1 << 5)) != 0;
        bool psPressed = (report[11] & (1 << 0)) != 0;
        // DualSense extended: o clique do touchpad está no bit 0x02 do byte 11.
        bool touchpadPressed = (report[11] & (1 << 1)) != 0;

        MapToXboxController(dpadUp, dpadDown, dpadLeft, dpadRight, squarePressed, crossPressed, circlePressed, trianglePressed,
                            l1Pressed, r1Pressed, l3Pressed, r3Pressed, createPressed, optionsPressed, psPressed, touchpadPressed,
                            lTrigger, rTrigger, xboxLeftStickX, xboxLeftStickY, xboxRightStickX, xboxRightStickY);
    }

    // Aplica os remapeamentos e envia um estado completo ao controle virtual Xbox.
    private static void MapToXboxController(bool dpadUp, bool dpadDown, bool dpadLeft, bool dpadRight,
                                        bool squarePressed, bool crossPressed, bool circlePressed, bool trianglePressed,
                                        bool l1Pressed, bool r1Pressed, bool l3Pressed, bool r3Pressed,
                                        bool createPressed, bool optionsPressed, bool psPressed, bool touchpadPressed,
                                        byte lTrigger, byte rTrigger,
                                        short xboxLeftStickX, short xboxLeftStickY,
                                        short xboxRightStickX, short xboxRightStickY)
    {
        // Recebe todos os estados brutos já interpretados e envia o estado Xbox agregado.
        // Reduz o deslocamento involuntário próximo ao centro de cada analógico.
        ApplyJoystickDeadzone(
            ref xboxLeftStickX,
            ref xboxLeftStickY,
            LeftJoystickDeadzonePercent,
            LeftJoystickAntiDeadzonePercent);
        ApplyJoystickDeadzone(
            ref xboxRightStickX,
            ref xboxRightStickY,
            RightJoystickDeadzonePercent,
            RightJoystickAntiDeadzonePercent);

        // Ajusta a faixa analógica independente dos gatilhos L2 e R2.
        lTrigger = ApplyTriggerDeadzone(
            lTrigger,
            LeftTriggerDeadzonePercent,
            LeftTriggerAntiDeadzonePercent);
        rTrigger = ApplyTriggerDeadzone(
            rTrigger,
            RightTriggerDeadzonePercent,
            RightTriggerAntiDeadzonePercent);

        // Lê o mapeamento atual sem bloquear alterações feitas pela interface.
        var mappings = Volatile.Read(ref _buttonMappings);

        // Detecta a borda de pressão do Config map sem repetir a troca enquanto o botão fica pressionado.
        ApplyConfigMapEdge(PlayStationButton.Cross, crossPressed);
        ApplyConfigMapEdge(PlayStationButton.Circle, circlePressed);
        ApplyConfigMapEdge(PlayStationButton.Square, squarePressed);
        ApplyConfigMapEdge(PlayStationButton.Triangle, trianglePressed);
        ApplyConfigMapEdge(PlayStationButton.L1, l1Pressed);
        ApplyConfigMapEdge(PlayStationButton.R1, r1Pressed);
        ApplyConfigMapEdge(PlayStationButton.L3, l3Pressed);
        ApplyConfigMapEdge(PlayStationButton.R3, r3Pressed);
        ApplyConfigMapEdge(PlayStationButton.Touchpad, touchpadPressed);
        ApplyConfigMapEdge(PlayStationButton.CreateShare, createPressed);
        ApplyConfigMapEdge(PlayStationButton.Options, optionsPressed);
        ApplyConfigMapEdge(PlayStationButton.PsButton, psPressed);
        ApplyConfigMapEdge(PlayStationButton.DpadUp, dpadUp);
        ApplyConfigMapEdge(PlayStationButton.DpadDown, dpadDown);
        ApplyConfigMapEdge(PlayStationButton.DpadLeft, dpadLeft);
        ApplyConfigMapEdge(PlayStationButton.DpadRight, dpadRight);

        // Para cada botão Xbox, combina todas as fontes PlayStation configuradas nele.
        foreach (var target in ButtonRemapping.XboxButtons)
        {
            if (target.Value is not Xbox360Button xboxButton)
            {
                continue;
            }

            var isPressed =
                (crossPressed && mappings[(int)PlayStationButton.Cross] == target.Value)
                || (circlePressed && mappings[(int)PlayStationButton.Circle] == target.Value)
                || (squarePressed && mappings[(int)PlayStationButton.Square] == target.Value)
                || (trianglePressed && mappings[(int)PlayStationButton.Triangle] == target.Value)
                || (l1Pressed && mappings[(int)PlayStationButton.L1] == target.Value)
                || (r1Pressed && mappings[(int)PlayStationButton.R1] == target.Value)
                || (l3Pressed && mappings[(int)PlayStationButton.L3] == target.Value)
                || (r3Pressed && mappings[(int)PlayStationButton.R3] == target.Value)
                // Um mapeamento vazio possui valor null e nunca ativa a saída.
                || (touchpadPressed && mappings[(int)PlayStationButton.Touchpad] == target.Value)
                || (createPressed && mappings[(int)PlayStationButton.CreateShare] == target.Value)
                || (optionsPressed && mappings[(int)PlayStationButton.Options] == target.Value)
                || (psPressed && mappings[(int)PlayStationButton.PsButton] == target.Value)
                || (dpadUp && mappings[(int)PlayStationButton.DpadUp] == target.Value)
                || (dpadDown && mappings[(int)PlayStationButton.DpadDown] == target.Value)
                || (dpadLeft && mappings[(int)PlayStationButton.DpadLeft] == target.Value)
                || (dpadRight && mappings[(int)PlayStationButton.DpadRight] == target.Value);
            XboxEmulator.SetButtonState(xboxButton, isPressed);
        }

        // Atualiza gatilhos e eixos analógicos no controlador Xbox virtual.
        XboxEmulator.SetTriggerState(Xbox360Slider.LeftTrigger, lTrigger);
        XboxEmulator.SetTriggerState(Xbox360Slider.RightTrigger, rTrigger);

        XboxEmulator.SetAxisState(Xbox360Axis.LeftThumbX, xboxLeftStickX);
        XboxEmulator.SetAxisState(Xbox360Axis.LeftThumbY, xboxLeftStickY);
        XboxEmulator.SetAxisState(Xbox360Axis.RightThumbX, xboxRightStickX);
        XboxEmulator.SetAxisState(Xbox360Axis.RightThumbY, xboxRightStickY);
    }

    // Remapeia radialmente a zona neutra e o limite externo sem alterar a direção do stick.
    private static void ApplyJoystickDeadzone(
        ref short x,
        ref short y,
        int deadzonePercent,
        int antiDeadzonePercent)
    {
        // Aplica simultaneamente zona neutra interna e limite externo radial.
        var magnitude = Math.Sqrt((double)x * x + (double)y * y);
        var innerMagnitude = short.MaxValue * deadzonePercent / 100.0;
        var outerMagnitude = short.MaxValue * (100.0 - antiDeadzonePercent) / 100.0;

        if (magnitude <= innerMagnitude)
        {
            x = 0;
            y = 0;
            return;
        }

        if (magnitude >= outerMagnitude)
        {
            if (magnitude == 0)
            {
                return;
            }

            var maximumScale = short.MaxValue / magnitude;
            x = (short)Math.Clamp(Math.Round(x * maximumScale), short.MinValue, short.MaxValue);
            y = (short)Math.Clamp(Math.Round(y * maximumScale), short.MinValue, short.MaxValue);
            return;
        }

        var usableMagnitude = outerMagnitude - innerMagnitude;
        var scaledMagnitude = usableMagnitude <= 0
            ? short.MaxValue
            : (magnitude - innerMagnitude) * short.MaxValue / usableMagnitude;
        var scale = scaledMagnitude / magnitude;
        x = (short)Math.Clamp(Math.Round(x * scale), short.MinValue, short.MaxValue);
        y = (short)Math.Clamp(Math.Round(y * scale), short.MinValue, short.MaxValue);
    }

    // Remapeia a pressão bruta do gatilho para o intervalo útil configurado.
    // Converte a zona útil do gatilho em uma faixa linear de 0 a 255.
    private static byte ApplyTriggerDeadzone(
        byte value,
        int deadzonePercent,
        int antiDeadzonePercent)
    {
        // Remapeia a faixa útil entre o limite interno e o limite externo configurados.
        double innerThreshold = byte.MaxValue * deadzonePercent / 100.0;
        double outerThreshold = byte.MaxValue * (100 - antiDeadzonePercent) / 100.0;

        if (value <= innerThreshold)
        {
            return 0;
        }

        if (value >= outerThreshold)
        {
            return byte.MaxValue;
        }

        double usableRange = outerThreshold - innerThreshold;
        double scaledValue = (value - innerThreshold) * byte.MaxValue / usableRange;
        return (byte)Math.Clamp(Math.Round(scaledValue), byte.MinValue, byte.MaxValue);
    }
    // Habilita ou desabilita a reprodução de vibração nos gatilhos adaptativos.
    public static void SetAdaptiveTriggerVibrationEnabled(bool enabled)
    {
        // Ativa efeitos especiais somente no DualSense e atualiza o relatório de saída.
        // Só permite efeitos nos gatilhos quando o modelo conectado é um DualSense.
        bool wasEnabled = _adaptiveTriggerVibrationEnabled;
        _adaptiveTriggerVibrationEnabled = enabled && _controller?.ProductID == 0x0CE6;
        if (_controllerType == ControllerType.DualSense)
        {
            SendDualSenseOutput(
                _lastLeftMotor,
                _lastRightMotor,
                updateTriggerEffects: wasEnabled || _adaptiveTriggerVibrationEnabled);
        }
    }

    // Valida e publica a intensidade configurada, reaplicando-a ao controle conectado.
    public static void SetAdaptiveTriggerIntensityPercent(int percent)
    {
        if (percent < 0 || percent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percent));
        }

        Volatile.Write(ref _adaptiveTriggerIntensityPercent, percent);
        if (_adaptiveTriggerVibrationEnabled && _controllerType == ControllerType.DualSense)
        {
            SendDualSenseOutput(_lastLeftMotor, _lastRightMotor, updateTriggerEffects: true);
        }
    }

    // Guarda os dois motores de vibração e encaminha o estado ao modelo detectado.
    public static void SendVibrationToController(byte leftMotor, byte rightMotor)
    {
        _lastLeftMotor = leftMotor;
        _lastRightMotor = rightMotor;

        if (_isXInputSource)
        {
            var userIndex = Volatile.Read(ref _xInputUserIndex);
            if (userIndex >= 0)
            {
                XInputControllerManager.SetVibration(userIndex, leftMotor, rightMotor);
            }

            return;
        }

        // Os valores salvos permitem reaplicar a vibração ao trocar opções ou reconectar.
        if (_outputStream == null)
        {
            return;
        }

        if (_controllerType == ControllerType.DualShock4)
        {
            SendVibrationToDualShock4(leftMotor, rightMotor);
            return;
        }

        SendDualSenseOutput(leftMotor, rightMotor, _adaptiveTriggerVibrationEnabled);
    }

    // Reaplica os motores salvos quando o polling detecta reconexão do mesmo slot XInput.
    public static void ReapplyXInputVibration()
    {
        if (!_isXInputSource)
        {
            return;
        }

        var userIndex = Volatile.Read(ref _xInputUserIndex);
        if (userIndex >= 0)
        {
            XInputControllerManager.SetVibration(
                userIndex,
                Volatile.Read(ref _lastLeftMotor),
                Volatile.Read(ref _lastRightMotor));
        }
    }

    // Monta e envia o relatório DualSense com motores, efeitos dos gatilhos e LEDs.
    private static void SendDualSenseOutput(byte leftMotor, byte rightMotor, bool updateTriggerEffects)
    {
        HidStream? outputStream = _outputStream;
        if (outputStream == null)
        {
            return;
        }

        int protocolReportLength = _isBluetooth ? 78 : 48;
        if (_outputReportLength < protocolReportLength)
        {
            ReportOutputWriteFailure(
                ref _dualSenseWriteFailureReported,
                $"DualSense output report is too short (bluetooth={_isBluetooth}, protocolLength={protocolReportLength}, deviceOutputLength={_outputReportLength})",
                new InvalidOperationException(
                    $"The controller requires a {protocolReportLength}-byte output report but HidSharp reports {_outputReportLength} bytes."));
            return;
        }

        byte[] vibrationReport = new byte[_outputReportLength];
        bool hasLightbarColor = Volatile.Read(ref _hasLightbarColor) != 0;
        Color lightbarColor = Color.FromArgb(Volatile.Read(ref _lightbarColorArgb));
        byte triggerMotorValue = GetAdaptiveTriggerMotorValue(leftMotor, rightMotor);
        // Máscara usada pelo DualSense para acender o LED correspondente ao slot ativo.
        byte playerLedMask = (byte)(1 << (_configurationPlayerLed - 1));

        // Os offsets e o CRC variam entre o relatório Bluetooth e o relatório USB.
        if (_isBluetooth)
        {
            vibrationReport[0] = 0x31;
            vibrationReport[1] = (byte)((_bluetoothSequence & 0x0F) << 4);
            _bluetoothSequence = (byte)((_bluetoothSequence + 1) & 0x0F);
            vibrationReport[2] = 0x10;
            vibrationReport[3] = updateTriggerEffects ? (byte)0x0F : (byte)0x02;
            vibrationReport[4] = 0x00;
            vibrationReport[5] = rightMotor;
            vibrationReport[6] = leftMotor;
            if (updateTriggerEffects)
            {
                WriteTriggerVibrationEffect(
                    vibrationReport, 13, triggerMotorValue);
                WriteTriggerVibrationEffect(
                    vibrationReport, 24, triggerMotorValue);
            }
            vibrationReport[41] = 0x04;
            vibrationReport[4] |= 0x10;
            vibrationReport[46] = playerLedMask;
            if (hasLightbarColor)
            {
                vibrationReport[4] |= 0x04;
                vibrationReport[47] = lightbarColor.R;
                vibrationReport[48] = lightbarColor.G;
                vibrationReport[49] = lightbarColor.B;
            }
            else
            {
                vibrationReport[48] = 0xFF;
            }

            SetBluetoothOutputCrc(vibrationReport);
        }
        else
        {
            vibrationReport[0] = 0x02;
            vibrationReport[1] = updateTriggerEffects ? (byte)0x0F : (byte)0x03;
            vibrationReport[2] = 0x00;
            vibrationReport[3] = rightMotor;
            vibrationReport[4] = leftMotor;
            if (updateTriggerEffects)
            {
                WriteTriggerVibrationEffect(
                    vibrationReport, 11, triggerMotorValue);
                WriteTriggerVibrationEffect(
                    vibrationReport, 22, triggerMotorValue);
            }
            if (hasLightbarColor)
            {
                vibrationReport[2] |= 0x04;
                vibrationReport[45] = lightbarColor.R;
                vibrationReport[46] = lightbarColor.G;
                vibrationReport[47] = lightbarColor.B;
            }
            // Posição USB dos LEDs de jogador; não afeta DualShock 4.
            if (_outputReportLength > 39)
            {
                vibrationReport[39] = playerLedMask;
            }
        }

        try
        {
            //Console.WriteLine($"Sending Vibration Report (Bluetooth: {_isBluetooth}): {BitConverter.ToString(vibrationReport)}");
            lock (_vibrationWriteLock)
            {
                outputStream.Write(vibrationReport, 0, vibrationReport.Length);
            }
            Interlocked.Exchange(ref _dualSenseWriteFailureReported, 0);
            //Console.WriteLine("Vibration command sent!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to send vibration data: {ex.Message}");
            ReportOutputWriteFailure(
                ref _dualSenseWriteFailureReported,
                $"Failed to send DualSense vibration output (bluetooth={_isBluetooth}, reportLength={vibrationReport.Length}, protocolLength={protocolReportLength}, deviceOutputLength={_outputReportLength})",
                ex);
        }
    }

    // Envia o comando especial que libera o controle dos LEDs no DualSense Bluetooth.
    private static bool SendDualSenseBluetoothLedRelease()
    {
        HidStream? outputStream = _outputStream;
        if (outputStream == null)
        {
            return false;
        }

        const int reportLength = 78;
        if (_outputReportLength < reportLength)
        {
            ReportOutputWriteFailure(
                ref _dualSenseWriteFailureReported,
                $"DualSense Bluetooth LED release report is too short (deviceOutputLength={_outputReportLength})",
                new InvalidOperationException(
                    $"The controller requires a {reportLength}-byte Bluetooth output report but HidSharp reports {_outputReportLength} bytes."));
            return false;
        }

        var report = new byte[_outputReportLength];
        report[0] = 0x31;
        report[1] = (byte)((_bluetoothSequence & 0x0F) << 4);
        _bluetoothSequence = (byte)((_bluetoothSequence + 1) & 0x0F);
        report[2] = 0x10;
        report[4] = 0x08;
        SetBluetoothOutputCrc(report);

        try
        {
            lock (_vibrationWriteLock)
            {
                outputStream.Write(report, 0, report.Length);
            }
            Interlocked.Exchange(ref _dualSenseWriteFailureReported, 0);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to release DualSense Bluetooth LEDs: {ex.Message}");
            ReportOutputWriteFailure(
                ref _dualSenseWriteFailureReported,
                "Failed to release DualSense Bluetooth LEDs",
                ex);
            return false;
        }
    }

    // Acrescenta ao relatório o CRC calculado com o prefixo exigido pelo transporte HID.
    private static void SetBluetoothOutputCrc(byte[] report)
    {
        const int crcOffset = 74;
        var crcData = new byte[75];
        crcData[0] = 0xA2;
        Array.Copy(report, 0, crcData, 1, crcOffset);
        var crcChecksum = NewCrc.ComputeDualShock4BluetoothCRC32(crcData);
        Array.Copy(BitConverter.GetBytes(crcChecksum), 0, report, crcOffset, 4);
    }

    // Seleciona o maior rumble ou, de 1% a 100%, uma intensidade fixa quando há vibração.
    private static byte GetAdaptiveTriggerMotorValue(byte leftMotor, byte rightMotor)
    {
        if (!_adaptiveTriggerVibrationEnabled)
        {
            return 0;
        }

        byte strongestMotor = Math.Max(leftMotor, rightMotor);
        if (strongestMotor == 0)
        {
            return 0;
        }

        int intensityPercent = Volatile.Read(ref _adaptiveTriggerIntensityPercent);
        if (intensityPercent == 0)
        {
            return strongestMotor;
        }

        return (byte)((intensityPercent * byte.MaxValue + 50) / 100);
    }

    // Evita repetir relatórios da mesma falha até que uma escrita volte a funcionar.
    private static void ReportOutputWriteFailure(ref int failureReported, string context, Exception exception)
    {
        // Registra uma falha apenas uma vez até que uma nova escrita seja bem-sucedida.
        if (Interlocked.Exchange(ref failureReported, 1) == 0)
        {
            CrashReporter.Report(context, exception);
        }
    }

    // Codifica a intensidade uniforme nos dez segmentos do efeito adaptativo DualSense.
    private static void WriteTriggerVibrationEffect(byte[] report, int offset, byte motorValue)
    {
        // Codifica a intensidade de resistência/vibração em um bloco do gatilho DualSense.
        Array.Clear(report, offset, 11);

        if (motorValue == 0)
        {
            report[offset] = 0x05;
            return;
        }

        report[offset] = 0x26;
        report[offset + 1] = 0xFF;
        report[offset + 2] = 0x03;

        int amplitude = Math.Max(1, (motorValue * 8 + 127) / 255);
        uint amplitudeZones = 0;
        for (int zone = 0; zone < 10; zone++)
        {
            amplitudeZones |= (uint)(amplitude - 1) << (zone * 3);
        }

        report[offset + 3] = (byte)amplitudeZones;
        report[offset + 4] = (byte)(amplitudeZones >> 8);
        report[offset + 5] = (byte)(amplitudeZones >> 16);
        report[offset + 6] = (byte)(amplitudeZones >> 24);
        report[offset + 9] = 40;
    }

    private static void SendVibrationToDualShock4(byte leftMotor, byte rightMotor)
    {
        // Constrói o relatório de vibração e barra de luz suportado pelo DualShock 4.
        int protocolReportLength = _isBluetooth ? 78 : 32;
        if (_outputReportLength < protocolReportLength)
        {
            ReportOutputWriteFailure(
                ref _dualShock4WriteFailureReported,
                $"DualShock 4 output report is too short (bluetooth={_isBluetooth}, protocolLength={protocolReportLength}, deviceOutputLength={_outputReportLength})",
                new InvalidOperationException(
                    $"The controller requires a {protocolReportLength}-byte output report but HidSharp reports {_outputReportLength} bytes."));
            return;
        }

        byte[] vibrationReport = new byte[_outputReportLength];
        bool hasLightbarColor = Volatile.Read(ref _hasLightbarColor) != 0;
        Color lightbarColor = Color.FromArgb(Volatile.Read(ref _lightbarColorArgb));

        if (_isBluetooth)
        {
            vibrationReport[0] = 0x11;
            vibrationReport[1] = 0xC0;
            vibrationReport[2] = 0x00;
            vibrationReport[3] = hasLightbarColor ? (byte)0x03 : (byte)0x01;
            vibrationReport[6] = rightMotor;
            vibrationReport[7] = leftMotor;
            if (hasLightbarColor)
            {
                vibrationReport[8] = lightbarColor.R;
                vibrationReport[9] = lightbarColor.G;
                vibrationReport[10] = lightbarColor.B;
            }

            SetBluetoothOutputCrc(vibrationReport);
        }
        else
        {
            vibrationReport[0] = 0x05;
            vibrationReport[1] = hasLightbarColor ? (byte)0x03 : (byte)0x01;
            vibrationReport[4] = rightMotor;
            vibrationReport[5] = leftMotor;
            if (hasLightbarColor)
            {
                vibrationReport[6] = lightbarColor.R;
                vibrationReport[7] = lightbarColor.G;
                vibrationReport[8] = lightbarColor.B;
            }
        }

        try
        {
            lock (_vibrationWriteLock)
            {
                _outputStream!.Write(vibrationReport, 0, vibrationReport.Length);
            }
            Interlocked.Exchange(ref _dualShock4WriteFailureReported, 0);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or ObjectDisposedException)
        {
            Console.WriteLine($"Failed to send DualShock 4 vibration data: {ex.Message}");
            ReportOutputWriteFailure(
                ref _dualShock4WriteFailureReported,
                $"Failed to send DualShock 4 vibration output (bluetooth={_isBluetooth}, reportLength={vibrationReport.Length}, protocolLength={protocolReportLength}, deviceOutputLength={_outputReportLength})",
                ex);
        }
    }

    // Localiza a interface HID com maior relatório de saída dentro da seleção salva.
    private static HidDevice? GetController(string? selectedInstanceId)
    {
        // Seleciona o endpoint compatível com maior relatório no controle escolhido.
        var deviceList = DeviceList.Local;
        var device = deviceList.GetHidDevices()
            .Where(d =>
            d.VendorID == 0x054C &&
            (d.ProductID == 0x0CE6 || d.ProductID == 0x05C4 || d.ProductID == 0x09CC) &&
            (string.IsNullOrWhiteSpace(selectedInstanceId)
                || string.Equals(
                    CloakManager.GetInstanceIdFromDevicePath(d.DevicePath),
                    selectedInstanceId,
                    StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(d => d.GetMaxOutputReportLength())
            .FirstOrDefault();

        return device;
    }

}
