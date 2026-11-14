using HidSharp;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using DualSenseCompanion;
using System.Reactive.Linq;
using System.Threading;

class ControllerManager
{
    private enum ControllerType
    {
        DualSense,
        DualShock4
    }

    private static HidDevice? _controller;
    private static HidStream? _inputStream;
    private static HidStream? _outputStream;
    private static readonly object _vibrationWriteLock = new();
    private static bool _isBluetooth = false;
    private static ControllerType _controllerType;
    private static int _outputReportLength;
    private static volatile bool _adaptiveTriggerVibrationEnabled;
    private static byte _lastLeftMotor;
    private static byte _lastRightMotor;
    private static byte _bluetoothSequence;
    private static int _dualSenseWriteFailureReported;
    private static int _dualShock4WriteFailureReported;

    public static void StartListening(CancellationToken cancellationToken, Action<bool> controllerDetected)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _controller = GetController();

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
            Interlocked.Exchange(ref _dualSenseWriteFailureReported, 0);
            Interlocked.Exchange(ref _dualShock4WriteFailureReported, 0);
            controllerDetected(_controllerType == ControllerType.DualSense);

            byte[] inputReport = new byte[inputReportLength];

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    int bytesRead = _inputStream.Read(inputReport);
                    if (bytesRead > 0)
                    {
                        ProcessInput(inputReport);
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

    public static void StopListening()
    {
        _inputStream?.Dispose();
        _outputStream?.Dispose();
    }

    private static void ProcessInput(byte[] report)
    {
        if (_controllerType == ControllerType.DualShock4)
        {
            ProcessDualShock4Input(report);
            return;
        }

        if (report.Length < 10)
        {
            Console.WriteLine("Invalid report length.");
            return;
        }

        byte reportId = report[0];

        if (reportId == 0x01)
        {
            ProcessStandardInput(report);
        }
        else if (reportId == 0x31)
        {
            ProcessExtendedInput(report);
        }
        else
        {
            Console.WriteLine($"Unknown report type");
        }
    }

    private static void ProcessStandardInput(byte[] report)
    {   
        //Offsets for BT/USB
        int dpadOffset = _isBluetooth ? 5 : 8;
        int buttonOffset = _isBluetooth ? 5 : 8;
        int shoulderOffset = _isBluetooth ? 6 : 9;
        int specialButtonOffset = _isBluetooth ? 7 : 10;
        int triggerOffset = _isBluetooth ? 8 : 5;

        //Sticks
        byte leftStickX = report[1];
        byte leftStickY = report[2];
        byte rightStickX = report[3];
        byte rightStickY = report[4];

        //Convert to Xbox stick ranges
        short xboxLeftStickX = (short)((leftStickX - 128) * 258);
        short xboxLeftStickY = (short)((127 - leftStickY) * 258);
        short xboxRightStickX = (short)((rightStickX - 128) * 258);
        short xboxRightStickY = (short)((127 - rightStickY) * 258);

        //Prevent stick values from flipping at the edges
        if (leftStickX == 0) xboxLeftStickX = -32768;
        if (leftStickX == 255) xboxLeftStickX = 32767;
        if (leftStickY == 0) xboxLeftStickY = 32767;
        if (leftStickY == 255) xboxLeftStickY = -32768;

        if (rightStickX == 0) xboxRightStickX = -32768;
        if (rightStickX == 255) xboxRightStickX = 32767;
        if (rightStickY == 0) xboxRightStickY = 32767;
        if (rightStickY == 255) xboxRightStickY = -32768;

        //Triggers
        byte lTrigger = report[triggerOffset];
        byte rTrigger = report[triggerOffset + 1];

        //D-Pad
        byte dpadState = (byte)(report[dpadOffset] & 0x0F);
        bool dpadUp = dpadState == 0 || dpadState == 1 || dpadState == 7;
        bool dpadRight = dpadState == 1 || dpadState == 2 || dpadState == 3;
        bool dpadDown = dpadState == 3 || dpadState == 4 || dpadState == 5;
        bool dpadLeft = dpadState == 5 || dpadState == 6 || dpadState == 7;

        //Buttons
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

        MapToXboxController(dpadUp, dpadDown, dpadLeft, dpadRight, squarePressed, crossPressed, circlePressed, trianglePressed,
                            l1Pressed, r1Pressed, l3Pressed, r3Pressed, createPressed, optionsPressed, psPressed,
                            lTrigger, rTrigger, xboxLeftStickX, xboxLeftStickY, xboxRightStickX, xboxRightStickY);
    }

    private static void ProcessDualShock4Input(byte[] report)
    {
        int stickOffset;
        int triggerOffset;
        int buttonOffset;
        int shoulderOffset;
        int specialButtonOffset;

        if (report[0] == 0x01)
        {
            stickOffset = 1;
            triggerOffset = 8;
            buttonOffset = 5;
            shoulderOffset = 6;
            specialButtonOffset = 7;
        }
        else if (report[0] == 0x11)
        {
            if (report.Length < 11)
            {
                Console.WriteLine("Invalid DualShock 4 Bluetooth report length.");
                return;
            }

            stickOffset = 2;
            triggerOffset = 6;
            buttonOffset = 8;
            shoulderOffset = 9;
            specialButtonOffset = 10;
            _isBluetooth = true;
        }
        else
        {
            Console.WriteLine($"Unknown DualShock 4 report type: 0x{report[0]:X2}");
            return;
        }

        int requiredLength = Math.Max(specialButtonOffset, triggerOffset + 1) + 1;
        if (report.Length < requiredLength)
        {
            Console.WriteLine("Invalid DualShock 4 report length.");
            return;
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

        MapToXboxController(dpadUp, dpadDown, dpadLeft, dpadRight, squarePressed, crossPressed, circlePressed, trianglePressed,
                            l1Pressed, r1Pressed, l3Pressed, r3Pressed, sharePressed, optionsPressed, psPressed,
                            report[triggerOffset], report[triggerOffset + 1],
                            xboxLeftStickX, xboxLeftStickY, xboxRightStickX, xboxRightStickY);
    }

    private static short ConvertStickX(byte value)
    {
        return value switch
        {
            0 => short.MinValue,
            255 => short.MaxValue,
            _ => (short)((value - 128) * 258)
        };
    }

    private static short ConvertStickY(byte value)
    {
        return value switch
        {
            0 => short.MaxValue,
            255 => short.MinValue,
            _ => (short)((127 - value) * 258)
        };
    }



    private static void ProcessExtendedInput(byte[] report)
    {
        //No need for offsets since we are currently using extended input report only in BT
        //Sticks
        byte leftStickX = report[2];
        byte leftStickY = report[3];
        byte rightStickX = report[4];
        byte rightStickY = report[5];

        //Convert to Xbox stick ranges
        short xboxLeftStickX = (short)((leftStickX - 128) * 258);
        short xboxLeftStickY = (short)((127 - leftStickY) * 258);
        short xboxRightStickX = (short)((rightStickX - 128) * 258);
        short xboxRightStickY = (short)((127 - rightStickY) * 258);

        //Prevent stick values from flipping at the edges
        if (leftStickX == 0) xboxLeftStickX = -32768;
        if (leftStickX == 255) xboxLeftStickX = 32767;
        if (leftStickY == 0) xboxLeftStickY = 32767;
        if (leftStickY == 255) xboxLeftStickY = -32768;

        if (rightStickX == 0) xboxRightStickX = -32768;
        if (rightStickX == 255) xboxRightStickX = 32767;
        if (rightStickY == 0) xboxRightStickY = 32767;
        if (rightStickY == 255) xboxRightStickY = -32768;

        //Triggers
        byte lTrigger = report[6];
        byte rTrigger = report[7];

        //D-Pad
        byte dpadState = (byte)(report[9] & 0x0F);
        bool dpadUp = dpadState == 0 || dpadState == 1 || dpadState == 7;
        bool dpadRight = dpadState == 1 || dpadState == 2 || dpadState == 3;
        bool dpadDown = dpadState == 3 || dpadState == 4 || dpadState == 5;
        bool dpadLeft = dpadState == 5 || dpadState == 6 || dpadState == 7;

        //Buttons
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

        //bool touchpadPressed = (report[11] & 0x02) != 0; //something for later :)

        MapToXboxController(dpadUp, dpadDown, dpadLeft, dpadRight, squarePressed, crossPressed, circlePressed, trianglePressed,
                            l1Pressed, r1Pressed, l3Pressed, r3Pressed, createPressed, optionsPressed, psPressed,
                            lTrigger, rTrigger, xboxLeftStickX, xboxLeftStickY, xboxRightStickX, xboxRightStickY);
    }

    private static void MapToXboxController(bool dpadUp, bool dpadDown, bool dpadLeft, bool dpadRight,
                                        bool squarePressed, bool crossPressed, bool circlePressed, bool trianglePressed,
                                        bool l1Pressed, bool r1Pressed, bool l3Pressed, bool r3Pressed,
                                        bool createPressed, bool optionsPressed, bool psPressed,
                                        byte lTrigger, byte rTrigger,
                                        short xboxLeftStickX, short xboxLeftStickY,
                                        short xboxRightStickX, short xboxRightStickY)
    {
        XboxEmulator.SetButtonState(Xbox360Button.A, crossPressed);
        XboxEmulator.SetButtonState(Xbox360Button.B, circlePressed);
        XboxEmulator.SetButtonState(Xbox360Button.X, squarePressed);
        XboxEmulator.SetButtonState(Xbox360Button.Y, trianglePressed);

        XboxEmulator.SetButtonState(Xbox360Button.LeftShoulder, l1Pressed);
        XboxEmulator.SetButtonState(Xbox360Button.RightShoulder, r1Pressed);
        XboxEmulator.SetButtonState(Xbox360Button.LeftThumb, l3Pressed);
        XboxEmulator.SetButtonState(Xbox360Button.RightThumb, r3Pressed);
        XboxEmulator.SetButtonState(Xbox360Button.Start, optionsPressed);
        XboxEmulator.SetButtonState(Xbox360Button.Back, createPressed);
        XboxEmulator.SetButtonState(Xbox360Button.Guide, psPressed);

        XboxEmulator.SetButtonState(Xbox360Button.Up, dpadUp);
        XboxEmulator.SetButtonState(Xbox360Button.Down, dpadDown);
        XboxEmulator.SetButtonState(Xbox360Button.Left, dpadLeft);
        XboxEmulator.SetButtonState(Xbox360Button.Right, dpadRight);

        XboxEmulator.SetTriggerState(Xbox360Slider.LeftTrigger, lTrigger);
        XboxEmulator.SetTriggerState(Xbox360Slider.RightTrigger, rTrigger);

        XboxEmulator.SetAxisState(Xbox360Axis.LeftThumbX, xboxLeftStickX);
        XboxEmulator.SetAxisState(Xbox360Axis.LeftThumbY, xboxLeftStickY);
        XboxEmulator.SetAxisState(Xbox360Axis.RightThumbX, xboxRightStickX);
        XboxEmulator.SetAxisState(Xbox360Axis.RightThumbY, xboxRightStickY);
    }


    //old function(only handles the basic report type)
    //private static void ProcessInput(byte[] report)
    //{
    //    if (report.Length < 10)
    //    {
    //        Console.WriteLine("Invalid report length.");
    //        return;
    //    }

    //    int dpadOffset = _isBluetooth ? 5 : 8;
    //    int buttonOffset = _isBluetooth ? 5 : 8;
    //    int shoulderOffset = _isBluetooth ? 6 : 9;
    //    int specialButtonOffset = _isBluetooth ? 7 : 10;
    //    int triggerOffset = _isBluetooth ? 8 : 5;

    //    //Same for USB & Bluetooth
    //    byte leftStickX = report[1];
    //    byte leftStickY = report[2];
    //    byte rightStickX = report[3];
    //    byte rightStickY = report[4];

    //    short xboxLeftStickX = (short)((leftStickX - 128) * 258);
    //    short xboxLeftStickY = (short)((127 - leftStickY) * 258);
    //    short xboxRightStickX = (short)((rightStickX - 128) * 258);
    //    short xboxRightStickY = (short)((127 - rightStickY) * 258);

    //    //Prevent stick values from flipping at the edges
    //    if (leftStickX == 0) xboxLeftStickX = -32768;
    //    if (leftStickX == 255) xboxLeftStickX = 32767;
    //    if (leftStickY == 0) xboxLeftStickY = 32767;
    //    if (leftStickY == 255) xboxLeftStickY = -32768;

    //    if (rightStickX == 0) xboxRightStickX = -32768;
    //    if (rightStickX == 255) xboxRightStickX = 32767;
    //    if (rightStickY == 0) xboxRightStickY = 32767;
    //    if (rightStickY == 255) xboxRightStickY = -32768;

    //    byte lTrigger = report[triggerOffset];
    //    byte rTrigger = report[triggerOffset + 1];

    //    byte dpadState = (byte)(report[dpadOffset] & 0x0F);
    //    bool dpadUp = dpadState == 0 || dpadState == 1 || dpadState == 7;
    //    bool dpadRight = dpadState == 1 || dpadState == 2 || dpadState == 3;
    //    bool dpadDown = dpadState == 3 || dpadState == 4 || dpadState == 5;
    //    bool dpadLeft = dpadState == 5 || dpadState == 6 || dpadState == 7;

    //    bool squarePressed = (report[buttonOffset] & 0x10) != 0;
    //    bool crossPressed = (report[buttonOffset] & 0x20) != 0;
    //    bool circlePressed = (report[buttonOffset] & 0x40) != 0;
    //    bool trianglePressed = (report[buttonOffset] & 0x80) != 0;

    //    bool l1Pressed = (report[shoulderOffset] & 0x01) != 0;
    //    bool r1Pressed = (report[shoulderOffset] & 0x02) != 0;
    //    bool l3Pressed = (report[shoulderOffset] & 0x40) != 0;
    //    bool r3Pressed = (report[shoulderOffset] & 0x80) != 0;
    //    bool createPressed = (report[shoulderOffset] & 0x10) != 0;
    //    bool optionsPressed = (report[shoulderOffset] & 0x20) != 0;

    //    bool psPressed = (report[specialButtonOffset] & 0x01) != 0;

    //    XboxEmulator.SetButtonState(Xbox360Button.A, crossPressed);
    //    XboxEmulator.SetButtonState(Xbox360Button.B, circlePressed);
    //    XboxEmulator.SetButtonState(Xbox360Button.X, squarePressed);
    //    XboxEmulator.SetButtonState(Xbox360Button.Y, trianglePressed);

    //    XboxEmulator.SetButtonState(Xbox360Button.LeftShoulder, l1Pressed);
    //    XboxEmulator.SetButtonState(Xbox360Button.RightShoulder, r1Pressed);
    //    XboxEmulator.SetButtonState(Xbox360Button.LeftThumb, l3Pressed);
    //    XboxEmulator.SetButtonState(Xbox360Button.RightThumb, r3Pressed);
    //    XboxEmulator.SetButtonState(Xbox360Button.Start, optionsPressed);
    //    XboxEmulator.SetButtonState(Xbox360Button.Back, createPressed);
    //    XboxEmulator.SetButtonState(Xbox360Button.Guide, psPressed);

    //    XboxEmulator.SetButtonState(Xbox360Button.Up, dpadUp);
    //    XboxEmulator.SetButtonState(Xbox360Button.Down, dpadDown);
    //    XboxEmulator.SetButtonState(Xbox360Button.Left, dpadLeft);
    //    XboxEmulator.SetButtonState(Xbox360Button.Right, dpadRight);

    //    XboxEmulator.SetTriggerState(Xbox360Slider.LeftTrigger, lTrigger);
    //    XboxEmulator.SetTriggerState(Xbox360Slider.RightTrigger, rTrigger);

    //    XboxEmulator.SetAxisState(Xbox360Axis.LeftThumbX, xboxLeftStickX);
    //    XboxEmulator.SetAxisState(Xbox360Axis.LeftThumbY, xboxLeftStickY);
    //    XboxEmulator.SetAxisState(Xbox360Axis.RightThumbX, xboxRightStickX);
    //    XboxEmulator.SetAxisState(Xbox360Axis.RightThumbY, xboxRightStickY);
    //}

    public static void InitializeVibration()
    {
        XboxEmulator.OnVibrationChanged -= SendVibrationToController;
        XboxEmulator.OnVibrationChanged += SendVibrationToController;
    }

    public static void SetAdaptiveTriggerVibrationEnabled(bool enabled)
    {
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

    public static void SendVibrationToController(byte leftMotor, byte rightMotor)
    {
        _lastLeftMotor = leftMotor;
        _lastRightMotor = rightMotor;

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
                    vibrationReport, 13, _adaptiveTriggerVibrationEnabled ? rightMotor : (byte)0);
                WriteTriggerVibrationEffect(
                    vibrationReport, 24, _adaptiveTriggerVibrationEnabled ? leftMotor : (byte)0);
            }
            vibrationReport[41] = 0x04;
            vibrationReport[43] = 0x02;
            vibrationReport[44] = 0x02;
            vibrationReport[48] = 0xFF;

            byte[] crcData = new byte[75];
            crcData[0] = 0xA2;
            Array.Copy(vibrationReport, 0, crcData, 1, 74);
            uint crcChecksum = NewCrc.ComputeDualShock4BluetoothCRC32(crcData);
            byte[] checksumBytes = BitConverter.GetBytes(crcChecksum);
            Array.Copy(checksumBytes, 0, vibrationReport, 74, 4);
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
                    vibrationReport, 11, _adaptiveTriggerVibrationEnabled ? rightMotor : (byte)0);
                WriteTriggerVibrationEffect(
                    vibrationReport, 22, _adaptiveTriggerVibrationEnabled ? leftMotor : (byte)0);
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

    private static void ReportOutputWriteFailure(ref int failureReported, string context, Exception exception)
    {
        if (Interlocked.Exchange(ref failureReported, 1) == 0)
        {
            CrashReporter.Report(context, exception);
        }
    }

    private static void WriteTriggerVibrationEffect(byte[] report, int offset, byte motorValue)
    {
        Array.Clear(report, offset, 11);

        if (motorValue == 0)
        {
            report[offset] = 0x05;
            return;
        }

        report[offset] = 0x26;
        report[offset + 1] = 0xFF;
        report[offset + 2] = 0x03;

        int amplitude = (motorValue * 8 + 254) / 255;
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

        if (_isBluetooth)
        {
            vibrationReport[0] = 0x11;
            vibrationReport[1] = 0xC0;
            vibrationReport[2] = 0xA0;
            vibrationReport[3] = 0x01;
            vibrationReport[6] = rightMotor;
            vibrationReport[7] = leftMotor;

            byte[] crcData = new byte[75];
            crcData[0] = 0xA2;
            Array.Copy(vibrationReport, 0, crcData, 1, 74);
            uint crcChecksum = NewCrc.ComputeDualShock4BluetoothCRC32(crcData);
            Array.Copy(BitConverter.GetBytes(crcChecksum), 0, vibrationReport, 74, 4);
        }
        else
        {
            vibrationReport[0] = 0x05;
            vibrationReport[1] = 0x01;
            vibrationReport[4] = rightMotor;
            vibrationReport[5] = leftMotor;
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

    private static HidDevice? GetController()
    {
        var deviceList = DeviceList.Local;
        var device = deviceList.GetHidDevices()
            .Where(d =>
            d.VendorID == 0x054C &&
            (d.ProductID == 0x0CE6 || d.ProductID == 0x05C4 || d.ProductID == 0x09CC))
            .OrderByDescending(d => d.GetMaxOutputReportLength())
            .FirstOrDefault();

        return device;
    }

}
