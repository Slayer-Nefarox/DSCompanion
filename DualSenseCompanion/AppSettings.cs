using System.Drawing;
using System.Globalization;
using Nefarius.ViGEm.Client.Targets.Xbox360;

internal sealed class AppSettings
{
    public const int MaximumJoystickDeadzonePercent = 50;

    public static string ConfigPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "config.txt");

    public bool ControllerEmulationEnabled { get; set; } = true;
    public bool AdaptiveTriggerVibrationEnabled { get; set; }
    public int LeftJoystickDeadzonePercent { get; set; }
    public int RightJoystickDeadzonePercent { get; set; }
    public Color? LightbarColor { get; set; }
    public Dictionary<PlayStationButton, Xbox360Button> ButtonMappings { get; set; } =
        ButtonRemapping.CreateDefaultMappings();

    public static AppSettings Load(out string? error, out bool configExists)
    {
        var settings = new AppSettings();
        error = null;
        configExists = File.Exists(ConfigPath);

        if (!configExists)
        {
            return settings;
        }

        try
        {
            var lineNumber = 0;
            foreach (var rawLine in File.ReadAllLines(ConfigPath))
            {
                lineNumber++;
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    throw new FormatException($"Invalid setting on line {lineNumber}.");
                }

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();
                if (key.StartsWith("ButtonMapping.", StringComparison.Ordinal))
                {
                    var sourceName = key["ButtonMapping.".Length..];
                    if (!Enum.TryParse(sourceName, true, out PlayStationButton source)
                        || !Enum.IsDefined(source)
                        || !ButtonRemapping.TryParseXboxButton(value, out var target)
                        || target == null)
                    {
                        throw new FormatException($"Invalid button mapping on line {lineNumber}.");
                    }

                    settings.ButtonMappings[source] = target;
                    continue;
                }

                switch (key)
                {
                    case nameof(ControllerEmulationEnabled):
                        if (!bool.TryParse(value, out var controllerEnabled))
                        {
                            throw new FormatException($"Invalid {key} value on line {lineNumber}.");
                        }

                        settings.ControllerEmulationEnabled = controllerEnabled;
                        break;
                    case nameof(AdaptiveTriggerVibrationEnabled):
                        if (!bool.TryParse(value, out var vibrationEnabled))
                        {
                            throw new FormatException($"Invalid {key} value on line {lineNumber}.");
                        }

                        settings.AdaptiveTriggerVibrationEnabled = vibrationEnabled;
                        break;
                    case "JoystickDeadzonePercent":
                        var legacyDeadzone = ParseDeadzone(value, key, lineNumber);
                        settings.LeftJoystickDeadzonePercent = legacyDeadzone;
                        settings.RightJoystickDeadzonePercent = legacyDeadzone;
                        break;
                    case nameof(LeftJoystickDeadzonePercent):
                        settings.LeftJoystickDeadzonePercent = ParseDeadzone(value, key, lineNumber);
                        break;
                    case nameof(RightJoystickDeadzonePercent):
                        settings.RightJoystickDeadzonePercent = ParseDeadzone(value, key, lineNumber);
                        break;
                    case nameof(LightbarColor):
                        if (value.Length == 0)
                        {
                            settings.LightbarColor = null;
                            break;
                        }

                        if (value.Length != 6
                            || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                        {
                            throw new FormatException($"Invalid {key} value on line {lineNumber}.");
                        }

                        settings.LightbarColor = Color.FromArgb(
                            255,
                            (rgb >> 16) & 0xFF,
                            (rgb >> 8) & 0xFF,
                            rgb & 0xFF);
                        break;
                }
            }
        }
        catch (IOException ex)
        {
            error = $"Could not read {ConfigPath}: {ex.Message}";
            return new AppSettings();
        }
        catch (UnauthorizedAccessException ex)
        {
            error = $"Could not read {ConfigPath}: {ex.Message}";
            return new AppSettings();
        }
        catch (FormatException ex)
        {
            error = $"The settings file is invalid: {ex.Message}";
            return new AppSettings();
        }

        return settings;
    }

    private static int ParseDeadzone(string value, string key, int lineNumber)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var deadzone)
            || deadzone < 0
            || deadzone > MaximumJoystickDeadzonePercent)
        {
            throw new FormatException($"Invalid {key} value on line {lineNumber}.");
        }

        return deadzone;
    }

    public void Save()
    {
        var lines = new List<string>
        {
            $"{nameof(ControllerEmulationEnabled)}={ControllerEmulationEnabled}",
            $"{nameof(AdaptiveTriggerVibrationEnabled)}={AdaptiveTriggerVibrationEnabled}",
            $"{nameof(LeftJoystickDeadzonePercent)}={LeftJoystickDeadzonePercent.ToString(CultureInfo.InvariantCulture)}",
            $"{nameof(RightJoystickDeadzonePercent)}={RightJoystickDeadzonePercent.ToString(CultureInfo.InvariantCulture)}",
            $"{nameof(LightbarColor)}={(LightbarColor is Color color ? $"{color.R:X2}{color.G:X2}{color.B:X2}" : string.Empty)}"
        };
        lines.AddRange(ButtonRemapping.PlayStationButtons.Select(button =>
            $"ButtonMapping.{button}={ButtonRemapping.GetConfigName(ButtonMappings[button])}"));
        File.WriteAllLines(ConfigPath, lines);
    }
}
