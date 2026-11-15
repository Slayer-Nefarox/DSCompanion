using Nefarius.ViGEm.Client.Targets.Xbox360;

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
    CreateShare,
    Options,
    PsButton,
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight
}

internal sealed class XboxButtonOption
{
    public Xbox360Button Value { get; }
    public string ConfigName { get; }
    private readonly string _displayName;

    public XboxButtonOption(Xbox360Button value, string configName, string displayName)
    {
        Value = value;
        ConfigName = configName;
        _displayName = displayName;
    }

    public override string ToString() => _displayName;
}

internal static class ButtonRemapping
{
    public static IReadOnlyList<PlayStationButton> PlayStationButtons { get; } =
        Array.AsReadOnly(Enum.GetValues<PlayStationButton>());

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

    public static Dictionary<PlayStationButton, Xbox360Button> CreateDefaultMappings()
    {
        return new Dictionary<PlayStationButton, Xbox360Button>
        {
            [PlayStationButton.Square] = Xbox360Button.X,
            [PlayStationButton.Cross] = Xbox360Button.A,
            [PlayStationButton.Circle] = Xbox360Button.B,
            [PlayStationButton.Triangle] = Xbox360Button.Y,
            [PlayStationButton.L1] = Xbox360Button.LeftShoulder,
            [PlayStationButton.R1] = Xbox360Button.RightShoulder,
            [PlayStationButton.L3] = Xbox360Button.LeftThumb,
            [PlayStationButton.R3] = Xbox360Button.RightThumb,
            [PlayStationButton.CreateShare] = Xbox360Button.Back,
            [PlayStationButton.Options] = Xbox360Button.Start,
            [PlayStationButton.PsButton] = Xbox360Button.Guide,
            [PlayStationButton.DpadUp] = Xbox360Button.Up,
            [PlayStationButton.DpadDown] = Xbox360Button.Down,
            [PlayStationButton.DpadLeft] = Xbox360Button.Left,
            [PlayStationButton.DpadRight] = Xbox360Button.Right
        };
    }

    public static Xbox360Button[] CreateDefaultTargetArray()
    {
        var mappings = CreateDefaultMappings();
        var targets = new Xbox360Button[PlayStationButtons.Count];
        foreach (var source in PlayStationButtons)
        {
            targets[(int)source] = mappings[source];
        }

        return targets;
    }

    public static bool IsXboxButton(Xbox360Button button) =>
        XboxButtons.Any(option => option.Value == button);

    public static bool TryParseXboxButton(string configName, out Xbox360Button? button)
    {
        var option = XboxButtons.FirstOrDefault(candidate =>
            string.Equals(candidate.ConfigName, configName, StringComparison.OrdinalIgnoreCase));
        button = option?.Value;
        return button != null;
    }

    public static string GetConfigName(Xbox360Button button) =>
        XboxButtons.First(option => option.Value == button).ConfigName;

    public static string GetDisplayName(PlayStationButton button) => button switch
    {
        PlayStationButton.CreateShare => "Create / Share",
        PlayStationButton.PsButton => "PS button",
        PlayStationButton.DpadUp => "D-pad Up",
        PlayStationButton.DpadDown => "D-pad Down",
        PlayStationButton.DpadLeft => "D-pad Left",
        PlayStationButton.DpadRight => "D-pad Right",
        _ => button.ToString()
    };
}
