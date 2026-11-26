using System.Drawing;
using System.Globalization;
using Nefarius.ViGEm.Client.Targets.Xbox360;

// Representa todas as opções persistentes de um único slot de configuração.
internal sealed class ConfigurationProfile
{
    public bool AdaptiveTriggerVibrationEnabled { get; set; }
    public int AdaptiveTriggerIntensityPercent { get; set; }
    public int LeftJoystickDeadzonePercent { get; set; }
    public int RightJoystickDeadzonePercent { get; set; }
    public int LeftJoystickAntiDeadzonePercent { get; set; }
    public int RightJoystickAntiDeadzonePercent { get; set; }
    public int LeftTriggerDeadzonePercent { get; set; }
    public int RightTriggerDeadzonePercent { get; set; }
    public int LeftTriggerAntiDeadzonePercent { get; set; }
    public int RightTriggerAntiDeadzonePercent { get; set; }
    public Color? LightbarColor { get; set; }
    public Dictionary<PlayStationButton, Xbox360Button?> ButtonMappings { get; set; } =
        ButtonRemapping.CreateDefaultMappings();
    // Fontes físicas configuradas para executar a troca dinâmica de perfil.
    public HashSet<PlayStationButton> ConfigMapMappings { get; set; } = new();
}

// Representa todas as opções persistentes da aplicação.
internal sealed class AppSettings
{
    // Limite máximo permitido para a zona neutra dos dois analógicos.
    public const int MaximumJoystickDeadzonePercent = 50;
    // Limite máximo permitido para deadzones e anti-deadzones dos gatilhos.
    public const int MaximumTriggerDeadzonePercent = 50;
    // Quantidade máxima de perfis configuráveis pelo usuário.
    public const int ConfigurationSlotCount = 4;

    // Caminho absoluto do arquivo de configuração criado ao lado do executável.
    public static string ConfigPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "config.txt");

    // Slots independentes; cada perfil contém todas as opções do programa.
    public ConfigurationProfile[] Profiles { get; } =
        Enumerable.Range(1, ConfigurationSlotCount)
            .Select(_ => new ConfigurationProfile())
            .ToArray();

    // Slot ativo. Sempre inicia como o último usado, mas nunca ativa a emulação automaticamente.
    private int _activeSlot = 1;
    public int ActiveSlot
    {
        get => _activeSlot;
        set => _activeSlot = Math.Clamp(value, 1, ConfigurationSlotCount);
    }

    // Perfil atualmente editado e aplicado pelo programa.
    public ConfigurationProfile ActiveProfile => Profiles[ActiveSlot - 1];

    // A emulação Xbox é uma ação temporária e sempre inicia desligada.
    public bool ControllerEmulationEnabled { get; set; }
    // Identificador do controle físico selecionado para o HidHide e a leitura HID.
    public string? SelectedControllerInstanceId { get; set; }
    // Indica se os motores de vibração devem ser encaminhados aos gatilhos.
    public bool AdaptiveTriggerVibrationEnabled
    { get => ActiveProfile.AdaptiveTriggerVibrationEnabled; set => ActiveProfile.AdaptiveTriggerVibrationEnabled = value; }
    public int AdaptiveTriggerIntensityPercent
    { get => ActiveProfile.AdaptiveTriggerIntensityPercent; set => ActiveProfile.AdaptiveTriggerIntensityPercent = value; }
    public int LeftJoystickDeadzonePercent
    { get => ActiveProfile.LeftJoystickDeadzonePercent; set => ActiveProfile.LeftJoystickDeadzonePercent = value; }
    public int RightJoystickDeadzonePercent
    { get => ActiveProfile.RightJoystickDeadzonePercent; set => ActiveProfile.RightJoystickDeadzonePercent = value; }
    public int LeftJoystickAntiDeadzonePercent
    { get => ActiveProfile.LeftJoystickAntiDeadzonePercent; set => ActiveProfile.LeftJoystickAntiDeadzonePercent = value; }
    public int RightJoystickAntiDeadzonePercent
    { get => ActiveProfile.RightJoystickAntiDeadzonePercent; set => ActiveProfile.RightJoystickAntiDeadzonePercent = value; }
    public int LeftTriggerDeadzonePercent
    { get => ActiveProfile.LeftTriggerDeadzonePercent; set => ActiveProfile.LeftTriggerDeadzonePercent = value; }
    public int RightTriggerDeadzonePercent
    { get => ActiveProfile.RightTriggerDeadzonePercent; set => ActiveProfile.RightTriggerDeadzonePercent = value; }
    public int LeftTriggerAntiDeadzonePercent
    { get => ActiveProfile.LeftTriggerAntiDeadzonePercent; set => ActiveProfile.LeftTriggerAntiDeadzonePercent = value; }
    public int RightTriggerAntiDeadzonePercent
    { get => ActiveProfile.RightTriggerAntiDeadzonePercent; set => ActiveProfile.RightTriggerAntiDeadzonePercent = value; }
    public Color? LightbarColor
    { get => ActiveProfile.LightbarColor; set => ActiveProfile.LightbarColor = value; }
    public Dictionary<PlayStationButton, Xbox360Button?> ButtonMappings
    { get => ActiveProfile.ButtonMappings; set => ActiveProfile.ButtonMappings = value; }
    // Fontes físicas configuradas para executar a troca dinâmica de perfil.
    public HashSet<PlayStationButton> ConfigMapMappings
    { get => ActiveProfile.ConfigMapMappings; set => ActiveProfile.ConfigMapMappings = value; }

    // Carrega as opções do disco e informa erros ou a ausência do arquivo.
    public static AppSettings Load(out string? error, out bool configExists)
    {
        var settings = new AppSettings();
        error = null;
        configExists = File.Exists(ConfigPath);

        // Quando não existe configuração, usa os valores padrão.
        if (!configExists)
        {
            return settings;
        }

        try
        {
            var lineNumber = 0;
            ConfigurationProfile currentProfile = settings.Profiles[0];
            foreach (var rawLine in File.ReadAllLines(ConfigPath))
            {
                lineNumber++;
                var line = rawLine.Trim();
                // Linhas vazias e iniciadas por # são ignoradas como comentários.
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                // Seções [Slot1] até [Slot4] alteram o perfil recebido pelas linhas seguintes.
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    var section = line[1..^1].Trim();
                    if (section.Equals("Global", StringComparison.OrdinalIgnoreCase))
                    {
                        currentProfile = settings.Profiles[settings.ActiveSlot - 1];
                    }
                    else if (section.StartsWith("Slot", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(section[4..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot)
                        && slot >= 1
                        && slot <= ConfigurationSlotCount)
                    {
                        currentProfile = settings.Profiles[slot - 1];
                    }
                    else
                    {
                        throw new FormatException($"Invalid configuration section on line {lineNumber}.");
                    }

                    continue;
                }

                var separator = line.IndexOf('=');
                // Toda configuração válida precisa ter uma chave separada por "=".
                if (separator <= 0)
                {
                    throw new FormatException($"Invalid setting on line {lineNumber}.");
                }

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();
                // O último slot ativo é global, não pertence a nenhum perfil.
                if (key.Equals("ActiveSlot", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var activeSlot)
                        || activeSlot < 1
                        || activeSlot > ConfigurationSlotCount)
                    {
                        throw new FormatException($"Invalid {key} value on line {lineNumber}.");
                    }

                    settings.ActiveSlot = activeSlot;
                    currentProfile = settings.ActiveProfile;
                    continue;
                }

                // Teclas especiais definem o remapeamento de um botão PlayStation.
                if (key.StartsWith("ButtonMapping.", StringComparison.Ordinal))
                {
                    var sourceName = key["ButtonMapping.".Length..];
                    if (!Enum.TryParse(sourceName, true, out PlayStationButton source)
                        || !Enum.IsDefined(source))
                    {
                        throw new FormatException($"Invalid button mapping on line {lineNumber}.");
                    }

                    if (string.Equals(value, "ConfigMap", StringComparison.OrdinalIgnoreCase))
                    {
                        currentProfile.ButtonMappings[source] = null;
                        currentProfile.ConfigMapMappings.Add(source);
                    }
                    else
                    {
                        currentProfile.ConfigMapMappings.Remove(source);
                        if (!ButtonRemapping.TryParseXboxButton(value, out var target))
                        {
                            throw new FormatException($"Invalid button mapping on line {lineNumber}.");
                        }

                        currentProfile.ButtonMappings[source] = target;
                    }
                    continue;
                }

                switch (key)
                {
                    case nameof(ControllerEmulationEnabled):
                        // Preferência deliberadamente não persistida: sempre inicia desligada.
                        break;
                    case nameof(SelectedControllerInstanceId):
                        settings.SelectedControllerInstanceId = value.Length == 0 ? null : value;
                        break;
                    case nameof(AdaptiveTriggerVibrationEnabled):
                        if (!bool.TryParse(value, out var vibrationEnabled))
                        {
                            throw new FormatException($"Invalid {key} value on line {lineNumber}.");
                        }

                        currentProfile.AdaptiveTriggerVibrationEnabled = vibrationEnabled;
                        break;
                    case nameof(AdaptiveTriggerIntensityPercent):
                        currentProfile.AdaptiveTriggerIntensityPercent = ParsePercent(value, key, lineNumber);
                        break;
                    case "JoystickDeadzonePercent":
                        var legacyDeadzone = ParseDeadzone(value, key, lineNumber);
                        currentProfile.LeftJoystickDeadzonePercent = legacyDeadzone;
                        currentProfile.RightJoystickDeadzonePercent = legacyDeadzone;
                        break;
                    case nameof(LeftJoystickDeadzonePercent):
                        currentProfile.LeftJoystickDeadzonePercent = ParseDeadzone(value, key, lineNumber);
                        break;
                    case nameof(RightJoystickDeadzonePercent):
                        currentProfile.RightJoystickDeadzonePercent = ParseDeadzone(value, key, lineNumber);
                        break;
                    case nameof(LeftJoystickAntiDeadzonePercent):
                        currentProfile.LeftJoystickAntiDeadzonePercent = ParseDeadzone(value, key, lineNumber);
                        break;
                    case nameof(RightJoystickAntiDeadzonePercent):
                        currentProfile.RightJoystickAntiDeadzonePercent = ParseDeadzone(value, key, lineNumber);
                        break;
                    case nameof(LeftTriggerDeadzonePercent):
                        currentProfile.LeftTriggerDeadzonePercent = ParseDeadzone(
                            value, key, lineNumber, MaximumTriggerDeadzonePercent);
                        break;
                    case nameof(RightTriggerDeadzonePercent):
                        currentProfile.RightTriggerDeadzonePercent = ParseDeadzone(
                            value, key, lineNumber, MaximumTriggerDeadzonePercent);
                        break;
                    case nameof(LeftTriggerAntiDeadzonePercent):
                        currentProfile.LeftTriggerAntiDeadzonePercent = ParseDeadzone(
                            value, key, lineNumber, MaximumTriggerDeadzonePercent);
                        break;
                    case nameof(RightTriggerAntiDeadzonePercent):
                        currentProfile.RightTriggerAntiDeadzonePercent = ParseDeadzone(
                            value, key, lineNumber, MaximumTriggerDeadzonePercent);
                        break;
                    case nameof(LightbarColor):
                        if (value.Length == 0)
                        {
                            currentProfile.LightbarColor = null;
                            break;
                        }

                        if (value.Length != 6
                            || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                        {
                            throw new FormatException($"Invalid {key} value on line {lineNumber}.");
                        }

                        currentProfile.LightbarColor = Color.FromArgb(
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

    private static int ParseDeadzone(
        string value,
        string key,
        int lineNumber,
        int maximumPercent = MaximumJoystickDeadzonePercent)
    {
        // Valida a mesma regra inteira para deadzones internas e externas.
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var deadzone)
            || deadzone < 0
            || deadzone > maximumPercent)
        {
            throw new FormatException($"Invalid {key} value on line {lineNumber}.");
        }

        return deadzone;
    }

    private static int ParsePercent(string value, string key, int lineNumber)
    {
        // Valida configurações percentuais que aceitam valores de 0 a 100.
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent)
            || percent < 0
            || percent > 100)
        {
            throw new FormatException($"Invalid {key} value on line {lineNumber}.");
        }

        return percent;
    }

    // Persiste as configurações atuais no arquivo config.txt.
    public void Save()
    {
        // Grava somente o slot ativo globalmente; ControllerEmulationEnabled nunca é persistida.
        var lines = new List<string>
        {
            "[Global]",
            $"# {ConfigurationSlotCount} configuration profiles are stored in this file.",
            $"ActiveSlot={ActiveSlot.ToString(CultureInfo.InvariantCulture)}",
            $"{nameof(SelectedControllerInstanceId)}={SelectedControllerInstanceId ?? string.Empty}"
        };

        for (var slotIndex = 0; slotIndex < ConfigurationSlotCount; slotIndex++)
        {
            // Cada slot é gravado em sua própria seção para preservar perfis independentes.
            var profile = Profiles[slotIndex];
            lines.Add(string.Empty);
            lines.Add($"[Slot{slotIndex + 1}]");
            lines.Add($"{nameof(AdaptiveTriggerVibrationEnabled)}={profile.AdaptiveTriggerVibrationEnabled}");
            lines.Add($"{nameof(AdaptiveTriggerIntensityPercent)}={profile.AdaptiveTriggerIntensityPercent.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"{nameof(LeftJoystickDeadzonePercent)}={profile.LeftJoystickDeadzonePercent.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"{nameof(RightJoystickDeadzonePercent)}={profile.RightJoystickDeadzonePercent.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"{nameof(LeftJoystickAntiDeadzonePercent)}={profile.LeftJoystickAntiDeadzonePercent.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"{nameof(RightJoystickAntiDeadzonePercent)}={profile.RightJoystickAntiDeadzonePercent.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"{nameof(LeftTriggerDeadzonePercent)}={profile.LeftTriggerDeadzonePercent.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"{nameof(RightTriggerDeadzonePercent)}={profile.RightTriggerDeadzonePercent.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"{nameof(LeftTriggerAntiDeadzonePercent)}={profile.LeftTriggerAntiDeadzonePercent.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"{nameof(RightTriggerAntiDeadzonePercent)}={profile.RightTriggerAntiDeadzonePercent.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"{nameof(LightbarColor)}={(profile.LightbarColor is Color color ? $"{color.R:X2}{color.G:X2}{color.B:X2}" : string.Empty)}");
            lines.AddRange(ButtonRemapping.PlayStationButtons.Select(button =>
                $"ButtonMapping.{button}={(profile.ConfigMapMappings.Contains(button)
                    ? "ConfigMap"
                    : ButtonRemapping.GetConfigName(profile.ButtonMappings[button]))}"));
        }

        File.WriteAllLines(ConfigPath, lines);
    }
}
