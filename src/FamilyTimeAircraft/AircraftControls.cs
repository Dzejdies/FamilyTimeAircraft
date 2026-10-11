using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace FamilyTimeAircraft;

public sealed class AircraftControls
{
    private const int CurrentControlLayout = 3;
    private const int InterimEnterControlLayout = 2;
    public ConfigEntry<Key> Menu { get; }
    public ConfigEntry<Key> Board { get; }
    public ConfigEntry<Key> AlternatePitchUp { get; }
    public ConfigEntry<Key> AlternatePitchDown { get; }
    public ConfigEntry<Key> AlternateRollLeft { get; }
    public ConfigEntry<Key> AlternateRollRight { get; }
    public ConfigEntry<Key> PitchUp { get; }
    public ConfigEntry<Key> PitchDown { get; }
    public ConfigEntry<Key> RollLeft { get; }
    public ConfigEntry<Key> RollRight { get; }
    public ConfigEntry<Key> YawLeft { get; }
    public ConfigEntry<Key> YawRight { get; }
    public ConfigEntry<Key> IncreaseThrottle { get; }
    public ConfigEntry<Key> DecreaseThrottle { get; }
    public ConfigEntry<Key> Brake { get; }

    public AircraftControls(ConfigFile config)
    {
        Menu = config.Bind("Controls", "Menu", Key.F8, "Open or close the aircraft flight controls. Construction uses the native building menu.");
        Board = config.Bind("Controls", "Board", Key.R, "Enter the aircraft under the crosshair or exit your current aircraft; deploy a parachute in flight. A permitted free pilot seat is preferred.");
        AlternatePitchUp = config.Bind("Controls", "AlternatePitchUp", Key.DownArrow, "Alternate nose-up key.");
        AlternatePitchDown = config.Bind("Controls", "AlternatePitchDown", Key.UpArrow, "Alternate nose-down key.");
        AlternateRollLeft = config.Bind("Controls", "AlternateRollLeft", Key.LeftArrow, "Alternate left-roll key.");
        AlternateRollRight = config.Bind("Controls", "AlternateRollRight", Key.RightArrow, "Alternate right-roll key.");
        PitchUp = config.Bind("Controls", "PitchUp", Key.S, "Pitch the aircraft nose upward.");
        PitchDown = config.Bind("Controls", "PitchDown", Key.W, "Pitch the aircraft nose downward.");
        RollLeft = config.Bind("Controls", "RollLeft", Key.A, "Roll the aircraft left.");
        RollRight = config.Bind("Controls", "RollRight", Key.D, "Roll the aircraft right.");
        YawLeft = config.Bind("Controls", "YawLeft", Key.Q, "Turn the aircraft nose left.");
        YawRight = config.Bind("Controls", "YawRight", Key.E, "Turn the aircraft nose right.");
        IncreaseThrottle = config.Bind("Controls", "IncreaseThrottle", Key.LeftShift, "Increase engine throttle while held.");
        DecreaseThrottle = config.Bind("Controls", "DecreaseThrottle", Key.LeftCtrl, "Reduce engine throttle and brake while held.");
        Brake = config.Bind("Controls", "Brake", Key.Space, "Apply the aircraft brake while held. Left Ctrl also brakes.");
        MigrateControlLayout(config);
    }

    private void MigrateControlLayout(ConfigFile config)
    {
        ConfigEntry<int> layout = config.Bind("Controls", "LayoutVersion", 0, "Aircraft controls layout migration version.");
        if (layout.Value >= CurrentControlLayout) return;
        if (Board.Value == Key.F || layout.Value == InterimEnterControlLayout && Board.Value == Key.E) Board.Value = Key.R;
        if (layout.Value == InterimEnterControlLayout && YawRight.Value == Key.R) YawRight.Value = Key.E;
        if (Board.Value == YawRight.Value && Board.Value != Key.None)
        {
            if (Board.Value == Key.R) YawRight.Value = Key.E;
            else Board.Value = Key.R;
        }
        layout.Value = CurrentControlLayout;
        config.Save();
    }

    public static bool Pressed(Keyboard keyboard, ConfigEntry<Key> key) => key.Value != Key.None && keyboard[key.Value].isPressed;
    public static float Axis(Keyboard keyboard, ConfigEntry<Key> positive, ConfigEntry<Key> negative) =>
        (Pressed(keyboard, positive) ? 1 : 0) - (Pressed(keyboard, negative) ? 1 : 0);
    public static float Axis(Keyboard keyboard, ConfigEntry<Key> positive, ConfigEntry<Key> alternatePositive, ConfigEntry<Key> negative, ConfigEntry<Key> alternateNegative) =>
        (Pressed(keyboard, positive) || Pressed(keyboard, alternatePositive) ? 1 : 0)
        - (Pressed(keyboard, negative) || Pressed(keyboard, alternateNegative) ? 1 : 0);
}
