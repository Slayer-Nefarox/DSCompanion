using System.Diagnostics;
using Nefarius.Drivers.HidHide;
using System.Management;
using System.Threading;

class CloakManager
{
    private static HidHideControlService _hidHide = new HidHideControlService();
    private static string? _controllerInstanceId;
    private static string _exePath = Process.GetCurrentProcess().MainModule?.FileName
        ?? Environment.ProcessPath
        ?? throw new InvalidOperationException("Unable to determine the application path.");

    public static void HideController(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_hidHide.IsInstalled)
        {
            Console.WriteLine("HidHide is not installed. Skipping controller hiding.");
            return;
        }

        _controllerInstanceId = FindControllerInstanceId(cancellationToken);

        if (string.IsNullOrEmpty(_controllerInstanceId))
        {
            Console.WriteLine("No supported PlayStation controller found.");
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine($"Attempting to block controller: {_controllerInstanceId}");

        WhitelistCurrentApp();

        if (!_hidHide.BlockedInstanceIds.Contains(_controllerInstanceId))
        {
            _hidHide.AddBlockedInstanceId(_controllerInstanceId);
            Console.WriteLine($"Added PlayStation controller ({_controllerInstanceId}) to HidHide block list.");
        }
        else
        {
            Console.WriteLine("PlayStation controller is already in the block list.");
        }

        _hidHide.IsActive = true;
        Console.WriteLine("PlayStation controller is now hidden!");
    }

    public static void UnhideController()
    {
        if (!_hidHide.IsInstalled)
        {
            Console.WriteLine("HidHide is not installed. Skipping controller unhiding.");
            return;
        }

        if (string.IsNullOrEmpty(_controllerInstanceId))
        {
            Console.WriteLine("No supported PlayStation controller found.");
            return;
        }

        if (_hidHide.BlockedInstanceIds.Contains(_controllerInstanceId))
        {
            _hidHide.RemoveBlockedInstanceId(_controllerInstanceId);
            Console.WriteLine("PlayStation controller removed from HidHide block list.");
        }

        _hidHide.IsActive = false;
        Console.WriteLine("PlayStation controller is now visible!");

        if (_hidHide.ApplicationPaths.Contains(_exePath))
        {
            _hidHide.RemoveApplicationPath(_exePath);
            Console.WriteLine("Removed application from HidHide whitelist.");
        }
    }

    //This version of the function is more optimized and actually waits for the user to plug in the controller
    public static string FindControllerInstanceId(CancellationToken cancellationToken)
    {

        Console.WriteLine("Looking for controller...");
        List<string> devices = new List<string>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            devices.Clear();
            devices.AddRange(GetAllHIDDevices());
           
            foreach (var device in devices)
            {

                if (IsSupportedController(device))
                {
                    Console.WriteLine($"Found PlayStation controller: {device}");
                    return device;
                }
            }

            cancellationToken.WaitHandle.WaitOne(1000);
        }

    }

    private static bool IsSupportedController(string deviceId)
    {
        return deviceId.Contains("VID_054C&PID_0CE6", StringComparison.OrdinalIgnoreCase)
            || deviceId.Contains("VID_054C&PID_05C4", StringComparison.OrdinalIgnoreCase)
            || deviceId.Contains("VID_054C&PID_09CC", StringComparison.OrdinalIgnoreCase)
            || deviceId.Contains("_VID&0002054C_PID&0CE6", StringComparison.OrdinalIgnoreCase)
            || deviceId.Contains("_VID&0002054C_PID&05C4", StringComparison.OrdinalIgnoreCase)
            || deviceId.Contains("_VID&0002054C_PID&09CC", StringComparison.OrdinalIgnoreCase);
    }

    public static List<string> GetAllHIDDevices()
    {
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
    private static void WhitelistCurrentApp()
    {
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