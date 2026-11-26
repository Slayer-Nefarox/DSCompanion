using Nefarius.ViGEm.Client.Targets.Xbox360;

// Lista os nomes internos dos botões físicos disponíveis no controle PlayStation.
internal enum PlayStationButton
{
    Square,
    Cross,
    Circle,
    Triangle,
    L1,
    R1,
    L3,
    R3,
    // Botão físico localizado abaixo dos botões de face no touchpad.
    Touchpad,
    CreateShare,
    Options,
    PsButton,
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight
}

// Associa um botão Xbox ao seu nome técnico e texto exibido na interface.
internal sealed class XboxButtonOption
{
    // Valor enum real utilizado pela biblioteca do controlador virtual.
    public Xbox360Button? Value { get; }
    // Texto estável usado no arquivo config.txt.
    public string ConfigName { get; }
    // Indica uma ação especial que alterna o slot em vez de emitir um botão Xbox.
    public bool IsConfigMap { get; }
    // Texto amigável apresentado ao usuário na interface.
    private readonly string _displayName;

    // Constrói uma opção associando valor interno, nome persistente e nome exibido.
    public XboxButtonOption(Xbox360Button? value, string configName, string displayName, bool isConfigMap = false)
    {
        Value = value;
        ConfigName = configName;
        _displayName = displayName;
        IsConfigMap = isConfigMap;
    }

    // Permite que a opção seja exibida diretamente em controles ComboBox.
    public override string ToString() => _displayName;
}

// Concentra a lista de botões e os mapeamentos padrão da aplicação.
internal static class ButtonRemapping
{
    // Nome persistente usado quando o usuário não quer emitir nenhum botão.
    public const string EmptyConfigName = "None";

    // Opção vazia exibida em todas as listas de destino do mapeamento.
    public static XboxButtonOption EmptyOption { get; } =
        new XboxButtonOption(null, EmptyConfigName, "None (ignored)");
    // Opção especial: um clique no botão físico cicla os slots de configuração.
    public static XboxButtonOption ConfigMapOption { get; } =
        new XboxButtonOption(null, "ConfigMap", "Config map", isConfigMap: true);

    // Lista somente leitura com todos os botões PlayStation que podem ser remapeados.
    public static IReadOnlyList<PlayStationButton> PlayStationButtons { get; } =
        Array.AsReadOnly(Enum.GetValues<PlayStationButton>());

    // Opções Xbox que podem ser associadas aos botões físicos na interface.
    public static IReadOnlyList<XboxButtonOption> XboxButtons { get; } =
        Array.AsReadOnly(new[]
        {
            new XboxButtonOption(Xbox360Button.A, "A", "A"),
            new XboxButtonOption(Xbox360Button.B, "B", "B"),
            new XboxButtonOption(Xbox360Button.X, "X", "X"),
            new XboxButtonOption(Xbox360Button.Y, "Y", "Y"),
            new XboxButtonOption(Xbox360Button.LeftShoulder, "LeftShoulder", "Left Shoulder"),
            new XboxButtonOption(Xbox360Button.RightShoulder, "RightShoulder", "Right Shoulder"),
            new XboxButtonOption(Xbox360Button.LeftThumb, "LeftThumb", "Left Thumb"),
            new XboxButtonOption(Xbox360Button.RightThumb, "RightThumb", "Right Thumb"),
            new XboxButtonOption(Xbox360Button.Start, "Start", "Start"),
            new XboxButtonOption(Xbox360Button.Back, "Back", "Back"),
            new XboxButtonOption(Xbox360Button.Guide, "Guide", "Guide"),
            new XboxButtonOption(Xbox360Button.Up, "Up", "D-pad Up"),
            new XboxButtonOption(Xbox360Button.Down, "Down", "D-pad Down"),
            new XboxButtonOption(Xbox360Button.Left, "Left", "D-pad Left"),
            new XboxButtonOption(Xbox360Button.Right, "Right", "D-pad Right")
        });

    // Cria uma cópia fresca dos mapeamentos padrão PlayStation -> Xbox.
    public static Dictionary<PlayStationButton, Xbox360Button?> CreateDefaultMappings()
    {
        // O touchpad inicia sem destino por padrão e pode ser configurado pelo usuário.
        return new Dictionary<PlayStationButton, Xbox360Button?>
        {
            [PlayStationButton.Square] = Xbox360Button.X,
            [PlayStationButton.Cross] = Xbox360Button.A,
            [PlayStationButton.Circle] = Xbox360Button.B,
            [PlayStationButton.Triangle] = Xbox360Button.Y,
            [PlayStationButton.L1] = Xbox360Button.LeftShoulder,
            [PlayStationButton.R1] = Xbox360Button.RightShoulder,
            [PlayStationButton.L3] = Xbox360Button.LeftThumb,
            [PlayStationButton.R3] = Xbox360Button.RightThumb,
            [PlayStationButton.Touchpad] = null,
            [PlayStationButton.CreateShare] = Xbox360Button.Back,
            [PlayStationButton.Options] = Xbox360Button.Start,
            [PlayStationButton.PsButton] = Xbox360Button.Guide,
            [PlayStationButton.DpadUp] = Xbox360Button.Up,
            [PlayStationButton.DpadDown] = Xbox360Button.Down,
            [PlayStationButton.DpadLeft] = Xbox360Button.Left,
            [PlayStationButton.DpadRight] = Xbox360Button.Right
        };
    }

    // Cria o vetor indexado usado pelo processamento rápido dos botões físicos.
    public static Xbox360Button?[] CreateDefaultTargetArray()
    {
        // Converte o dicionário padrão em um vetor indexado pela enumeração PlayStationButton.
        var mappings = CreateDefaultMappings();
        var targets = new Xbox360Button?[PlayStationButtons.Count];
        foreach (var source in PlayStationButtons)
        {
            targets[(int)source] = mappings[source];
        }

        return targets;
    }

    // Confere se o botão Xbox faz parte da lista permitida pela interface.
    public static bool IsXboxButton(Xbox360Button button) =>
        XboxButtons.Any(option => option.Value == button);

    // Recupera um botão Xbox pelo nome persistido no arquivo de configuração.
    public static bool TryParseXboxButton(string configName, out Xbox360Button? button)
    {
        // “Nenhum” representa um mapeamento desativado e é válido para qualquer botão físico.
        if (string.Equals(configName, EmptyConfigName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(configName, "Empty", StringComparison.OrdinalIgnoreCase))
        {
            button = null;
            return true;
        }

        // Procura uma opção pelo nome persistente, sem diferenciar maiúsculas e minúsculas.
        var option = XboxButtons.FirstOrDefault(candidate =>
            string.Equals(candidate.ConfigName, configName, StringComparison.OrdinalIgnoreCase));
        button = option?.Value;
        return button != null;
    }

    // Obtém o nome persistente correspondente ao enum Xbox informado.
    public static string GetConfigName(Xbox360Button? button) =>
        button is null
            ? EmptyConfigName
            : XboxButtons.First(option => option.Value == button).ConfigName;

    // Traduz nomes técnicos do enum em rótulos mais claros para a interface.
    public static string GetDisplayName(PlayStationButton button) => button switch
    {
        PlayStationButton.CreateShare => "Create / Share",
        PlayStationButton.PsButton => "PS button",
        PlayStationButton.Touchpad => "Touchpad",
        PlayStationButton.DpadUp => "D-pad Up",
        PlayStationButton.DpadDown => "D-pad Down",
        PlayStationButton.DpadLeft => "D-pad Left",
        PlayStationButton.DpadRight => "D-pad Right",
        _ => button.ToString()
    };

    // Adapta o rótulo do botão à origem PlayStation ou XInput selecionada.
    public static string GetDisplayName(PlayStationButton button, bool xInputSource)
    {
        if (!xInputSource)
        {
            return GetDisplayName(button);
        }

        return button switch
        {
            PlayStationButton.Cross => "A",
            PlayStationButton.Circle => "B",
            PlayStationButton.Square => "X",
            PlayStationButton.Triangle => "Y",
            PlayStationButton.L1 => "Left Shoulder",
            PlayStationButton.R1 => "Right Shoulder",
            PlayStationButton.L3 => "Left Stick Click",
            PlayStationButton.R3 => "Right Stick Click",
            PlayStationButton.Touchpad => "Touchpad (unavailable)",
            PlayStationButton.CreateShare => "Back",
            PlayStationButton.Options => "Start",
            PlayStationButton.PsButton => "Guide",
            _ => GetDisplayName(button)
        };
    }
}
