using System.Diagnostics;
using HidSharp;
using Nefarius.Drivers.HidHide;
using System.Management;
using System.Threading;

// Gerencia o HidHide para ocultar o controle físico enquanto o controle virtual está ativo.
class CloakManager
{
    // Mantém o serviço HidHide e os dados necessários para ocultar e restaurar o controle.
    private static HidHideControlService _hidHide = new HidHideControlService();
    // Guarda todos os identificadores adicionados para removê-los ao desativar a emulação.
    private static readonly HashSet<string> _controllerInstanceIds = new(StringComparer.OrdinalIgnoreCase);
    // Caminho do executável usado para adicionar uma exceção no HidHide.
    private static string _exePath = Process.GetCurrentProcess().MainModule?.FileName
        ?? Environment.ProcessPath
        ?? throw new InvalidOperationException("Unable to determine the application path.");

    // Oculta o controle físico no Windows depois de localizar o Device Instance ID.
    public static void HideController(CancellationToken cancellationToken, string controllerInstanceId)
    {
        // Interrompe imediatamente a operação quando o usuário solicitou cancelamento.
        cancellationToken.ThrowIfCancellationRequested();
        _controllerInstanceIds.Clear();
        _controllerInstanceIds.Add(controllerInstanceId);
        // Cancela a operação sem alterar o sistema caso o HidHide não esteja instalado.
        if (!_hidHide.IsInstalled)
        {
            Console.WriteLine("HidHide is not installed. Skipping controller hiding.");
            return;
        }

        if (string.IsNullOrEmpty(controllerInstanceId))
        {
            Console.WriteLine("No supported PlayStation controller found.");
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine($"Attempting to block controller: {controllerInstanceId}");

        // Permite que o próprio aplicativo continue vendo o dispositivo no sistema.
        WhitelistCurrentApp();

        // Adiciona o controlador à lista bloqueada apenas uma vez.
        if (!_hidHide.BlockedInstanceIds.Contains(controllerInstanceId))
        {
            _hidHide.AddBlockedInstanceId(controllerInstanceId);
            Console.WriteLine($"Added PlayStation controller ({controllerInstanceId}) to HidHide block list.");
        }
        else
        {
            Console.WriteLine("PlayStation controller is already in the block list.");
        }

        // Ativa o HidHide depois de configurar a lista e a exceção do aplicativo.
        _hidHide.IsActive = true;
        Console.WriteLine("PlayStation controller is now hidden!");
    }

    // Remove o bloqueio e a exceção do aplicativo para restaurar o controle físico.
    public static void UnhideController()
    {
        // Remove o bloqueio do controle e a exceção do aplicativo, quando aplicável.
        if (!_hidHide.IsInstalled)
        {
            Console.WriteLine("HidHide is not installed. Skipping controller unhiding.");
            return;
        }

        if (_controllerInstanceIds.Count == 0)
        {
            Console.WriteLine("No supported PlayStation controller found.");
            return;
        }

        foreach (var controllerInstanceId in _controllerInstanceIds)
        {
            if (_hidHide.BlockedInstanceIds.Contains(controllerInstanceId))
            {
                _hidHide.RemoveBlockedInstanceId(controllerInstanceId);
                Console.WriteLine($"Controller removed from HidHide block list: {controllerInstanceId}");
            }
        }

        _hidHide.IsActive = false;
        _controllerInstanceIds.Clear();
        Console.WriteLine("PlayStation controller is now visible!");

        if (_hidHide.ApplicationPaths.Contains(_exePath))
        {
            _hidHide.RemoveApplicationPath(_exePath);
            Console.WriteLine("Removed application from HidHide whitelist.");
        }
    }

    // Combina dispositivos PlayStation HID compatíveis e slots XInput conectados.
    public static IReadOnlyList<ControllerSelectionOption> GetSupportedControllers()
    {
        var supportedInstanceIds = GetAllHIDDevices()
            .Where(IsSupportedController)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var devicesByInstanceId = new Dictionary<string, (HidDevice Device, int OutputLength)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var device in DeviceList.Local.GetHidDevices())
        {
            if (device.VendorID != 0x054C
                || (device.ProductID != 0x0CE6
                    && device.ProductID != 0x05C4
                    && device.ProductID != 0x09CC))
            {
                continue;
            }

            var instanceId = GetInstanceIdFromDevicePath(device.DevicePath);
            if (instanceId == null || !supportedInstanceIds.Contains(instanceId))
            {
                continue;
            }

            var inputLength = device.GetMaxInputReportLength();
            var outputLength = device.GetMaxOutputReportLength();
            if (inputLength <= 0 || outputLength <= 0)
            {
                continue;
            }

            if (!devicesByInstanceId.TryGetValue(instanceId, out var current)
                || outputLength > current.OutputLength)
            {
                devicesByInstanceId[instanceId] = (device, outputLength);
            }
        }

        var controllers = devicesByInstanceId
            .Select(pair =>
            {
                var model = pair.Value.Device.ProductID == 0x0CE6 ? "DualSense" : "DualShock 4";
                var instanceSuffix = pair.Key.Split('\\').Last();
                if (instanceSuffix.Length > 8)
                {
                    instanceSuffix = instanceSuffix[^8..];
                }

                return new ControllerSelectionOption(
                    pair.Key,
                    $"{model} · {instanceSuffix}",
                    ControllerConnectionKind.PlayStationHid);
            })
            .ToList();
        controllers.AddRange(XInputControllerManager.GetConnectedControllers());
        return controllers
            .OrderBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // Oculta os dispositivos HID associados ao XInput enquanto a emulação está ativa.
    public static void HideXInputControllers(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var controllerInstanceIds = GetAllHIDDevices()
            .Where(IsXInputController)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (controllerInstanceIds.Length == 0)
        {
            throw new InvalidOperationException(
                "XInput is connected, but no XInput HID device identifiers were found for HidHide.");
        }

        _controllerInstanceIds.UnionWith(controllerInstanceIds);
        if (!_hidHide.IsInstalled)
        {
            throw new InvalidOperationException(
                "HidHide is not installed; XInput controller emulation cannot start without hiding the physical controller.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        WhitelistCurrentApp();

        foreach (var controllerInstanceId in controllerInstanceIds)
        {
            if (!_hidHide.BlockedInstanceIds.Contains(controllerInstanceId))
            {
                _hidHide.AddBlockedInstanceId(controllerInstanceId);
                Console.WriteLine($"Added XInput controller ({controllerInstanceId}) to HidHide block list.");
            }
        }

        _hidHide.IsActive = true;
        Console.WriteLine($"HidHide is active for {controllerInstanceIds.Length} XInput controller device(s).");
    }

    // Extrai do caminho HID o Device Instance ID usado pela seleção e pelo HidHide.
    internal static string? GetInstanceIdFromDevicePath(string devicePath)
    {
        const string devicePathPrefix = @"\\?\";
        if (devicePath.StartsWith(devicePathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            devicePath = devicePath[devicePathPrefix.Length..];
        }

        var pathParts = devicePath.Split('#');
        if (pathParts.Length < 3)
        {
            return null;
        }

        return string.Join("\\", pathParts.Take(3));
    }

    // Filtra IDs de dispositivo pelos identificadores oficiais dos controles PlayStation.
    private static bool IsSupportedController(string deviceId)
    {
        return deviceId.Contains("VID_054C&PID_0CE6", StringComparison.OrdinalIgnoreCase)
            || deviceId.Contains("VID_054C&PID_05C4", StringComparison.OrdinalIgnoreCase)
            || deviceId.Contains("VID_054C&PID_09CC", StringComparison.OrdinalIgnoreCase)
            || deviceId.Contains("_VID&0002054C_PID&0CE6", StringComparison.OrdinalIgnoreCase)
            || deviceId.Contains("_VID&0002054C_PID&05C4", StringComparison.OrdinalIgnoreCase)
            || deviceId.Contains("_VID&0002054C_PID&09CC", StringComparison.OrdinalIgnoreCase);
    }

    // Reconhece interfaces HID de XInput pelo marcador de interface do Windows.
    private static bool IsXInputController(string deviceId) =>
        deviceId.Contains("&IG_", StringComparison.OrdinalIgnoreCase);

    // Enumera todos os dispositivos HID visíveis pelo gerenciador WMI do Windows.
    public static List<string> GetAllHIDDevices()
    {
        // Lista que receberá os Device IDs encontrados pela consulta WMI.
        List<string> deviceList = new List<string>();

        try
        {
            using (var searcher = new ManagementObjectSearcher(@"SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE 'HID%'"))
            {
                foreach (ManagementObject device in searcher.Get())
                {
                    string deviceId = device["DeviceID"]?.ToString();
                    if (!string.IsNullOrEmpty(deviceId))
                    {
                        deviceList.Add(deviceId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving HID devices: {ex.Message}");
            CrashReporter.Report("Failed to enumerate HID devices", ex);
        }

        return deviceList;
    }
    // Garante que o HidHide continue permitindo que esta aplicação leia o controle.
    private static void WhitelistCurrentApp()
    {
        // Impede que o HidHide bloqueie a própria aplicação que utiliza o controle.
        if (!_hidHide.ApplicationPaths.Contains(_exePath))
        {
            _hidHide.AddApplicationPath(_exePath);
            Console.WriteLine($"Added {System.IO.Path.GetFileName(_exePath)} to HidHide whitelist.");
        }
        else
        {
            Console.WriteLine("Application is already whitelisted.");
        }
    }
}