using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

// Distingue a origem PlayStation HID dos controles acessados pelo runtime XInput.
internal enum ControllerConnectionKind
{
    PlayStationHid,
    XInput
}

// Dados exibidos no seletor e usados para reabrir a origem escolhida.
internal sealed record ControllerSelectionOption(
    string InstanceId,
    string DisplayName,
    ControllerConnectionKind Kind)
{
    public override string ToString() => DisplayName;
}

// Estado normalizado dos campos que o XInput fornece em cada leitura.
internal readonly record struct XInputGamepadState(
    ushort Buttons,
    byte LeftTrigger,
    byte RightTrigger,
    short LeftThumbX,
    short LeftThumbY,
    short RightThumbX,
    short RightThumbY);

internal static class XInputControllerManager
{
    private const int UserIndexCount = 4;
    private const uint DeviceNotConnected = 1167;
    private const string SelectionPrefix = "XInput:";
    private static readonly Lazy<XInputApi> Api = new(LoadApi);
    private static int _vibrationFailureReported;
    private static IntPtr _xInputLibrary;

    // Lista os slots XInput conectados e gera IDs estáveis apenas durante a sessão do Windows.
    public static IReadOnlyList<ControllerSelectionOption> GetConnectedControllers()
    {
        var controllers = new List<ControllerSelectionOption>();
        for (var userIndex = 0; userIndex < UserIndexCount; userIndex++)
        {
            if (TryReadState(userIndex, out _))
            {
                controllers.Add(new ControllerSelectionOption(
                    GetSelectionId(userIndex),
                    $"XInput controller {userIndex + 1}",
                    ControllerConnectionKind.XInput));
            }
        }

        return controllers;
    }

    // Permite que a interface identifique rapidamente uma seleção XInput válida.
    public static bool IsXInputSelection(string? selectionId) =>
        TryParseSelectionId(selectionId, out _);

    // Valida o formato salvo e converte o identificador XInput no índice de usuário.
    public static bool TryParseSelectionId(string? selectionId, out int userIndex)
    {
        userIndex = -1;
        if (selectionId == null
            || !selectionId.StartsWith(SelectionPrefix, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(
                selectionId[SelectionPrefix.Length..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out userIndex)
            || userIndex < 0
            || userIndex >= UserIndexCount)
        {
            userIndex = -1;
            return false;
        }

        if (!string.Equals(
            selectionId,
            GetSelectionId(userIndex),
            StringComparison.OrdinalIgnoreCase))
        {
            userIndex = -1;
            return false;
        }

        return true;
    }

    // Faz polling do slot selecionado e traduz cada estado para o controle Xbox virtual.
    public static void StartListening(
        CancellationToken cancellationToken,
        int userIndex,
        Action<int> controllerConnected)
    {
        if (userIndex < 0 || userIndex >= UserIndexCount)
        {
            throw new ArgumentOutOfRangeException(nameof(userIndex));
        }

        var connected = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!TryReadState(userIndex, out var state))
            {
                if (connected)
                {
                    Console.WriteLine($"XInput controller {userIndex + 1} disconnected.");
                }

                return;
            }

            if (!connected)
            {
                connected = true;
                ControllerManager.ReapplyXInputVibration();
                controllerConnected(userIndex);
            }

            ControllerManager.ProcessXInputInput(state);
            if (cancellationToken.WaitHandle.WaitOne(8))
            {
                return;
            }
        }
    }

    // Cria o identificador persistido para o slot XInput informado.
    private static string GetSelectionId(int userIndex) =>
        $"{SelectionPrefix}{userIndex.ToString(CultureInfo.InvariantCulture)}";

    // Lê um estado XInput e diferencia desconexão de falhas reais da API.
    private static bool TryReadState(int userIndex, out XInputGamepadState gamepadState)
    {
        var result = Api.Value.GetState((uint)userIndex, out var state);
        if (result == DeviceNotConnected)
        {
            gamepadState = default;
            return false;
        }

        if (result != 0)
        {
            throw new Win32Exception((int)result, $"XInputGetState failed for user index {userIndex}.");
        }

        gamepadState = new XInputGamepadState(
            state.Gamepad.Buttons,
            state.Gamepad.LeftTrigger,
            state.Gamepad.RightTrigger,
            state.Gamepad.LeftThumbX,
            state.Gamepad.LeftThumbY,
            state.Gamepad.RightThumbX,
            state.Gamepad.RightThumbY);
        return true;
    }

    // Converte os motores de 8 para 16 bits e envia vibração ao slot indicado.
    public static void SetVibration(int userIndex, byte leftMotor, byte rightMotor)
    {
        if (userIndex < 0 || userIndex >= UserIndexCount)
        {
            throw new ArgumentOutOfRangeException(nameof(userIndex));
        }

        var vibration = new NativeVibration(
            (ushort)(leftMotor * 257),
            (ushort)(rightMotor * 257));
        var result = Api.Value.SetState((uint)userIndex, ref vibration);
        if (result == DeviceNotConnected)
        {
            return;
        }

        if (result == 0)
        {
            Interlocked.Exchange(ref _vibrationFailureReported, 0);
        }
        else if (Interlocked.Exchange(ref _vibrationFailureReported, 1) == 0)
        {
            var exception = new Win32Exception(
                (int)result,
                $"XInputSetState failed for user index {userIndex}.");
            Console.WriteLine(exception.Message);
            CrashReporter.Report("Failed to send vibration to the XInput controller", exception);
        }
    }

    // Carrega a primeira DLL XInput disponível e resolve seus pontos de entrada.
    private static XInputApi LoadApi()
    {
        foreach (var libraryName in new[] { "xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll" })
        {
            if (!NativeLibrary.TryLoad(libraryName, out var library))
            {
                continue;
            }

            if (NativeLibrary.TryGetExport(library, "XInputGetState", out var getStateFunction)
                && NativeLibrary.TryGetExport(library, "XInputSetState", out var setStateFunction))
            {
                _xInputLibrary = library;
                return new XInputApi(
                    Marshal.GetDelegateForFunctionPointer<GetStateDelegate>(getStateFunction),
                    Marshal.GetDelegateForFunctionPointer<SetStateDelegate>(setStateFunction));
            }

            NativeLibrary.Free(library);
        }

        throw new DllNotFoundException(
            "Unable to load an XInput runtime (xinput1_4.dll, xinput1_3.dll, or xinput9_1_0.dll).");
    }

    // Os delegates devem usar a convenção de chamada da API Windows.
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetStateDelegate(uint userIndex, out NativeState state);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint SetStateDelegate(uint userIndex, ref NativeVibration vibration);

    private sealed record XInputApi(GetStateDelegate GetState, SetStateDelegate SetState);

    // Estruturas nativas devem manter o layout binário esperado pela API XInput.
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeState
    {
        public uint PacketNumber;
        public NativeGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftThumbX;
        public short LeftThumbY;
        public short RightThumbX;
        public short RightThumbY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeVibration(ushort leftMotorSpeed, ushort rightMotorSpeed)
    {
        public ushort LeftMotorSpeed = leftMotorSpeed;
        public ushort RightMotorSpeed = rightMotorSpeed;
    }
}
