using System.Collections;
using System;
using System.Linq;
using FamilyTimeAircraft.Core;
using GAT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Layouts;

namespace FamilyTimeAircraft.Diagnostics;

public static class AircraftKeyboardFixture
{
    public const float HoldSeconds = 0.15f;
    private static Keyboard? _ownedKeyboard;
    private static Mouse? _ownedMouse;
    private static Vector3? _aimTarget;

    public static IEnumerator ToggleSeat(Aircraft plane, Key boardKey)
    {
        EnsureKeyboard();
        Vector3 target = GameFlightEnvironment.ToUnity(plane.Position) + Vector3.up;
        Camera.main.transform.LookAt(target);
        SetKeys(boardKey);
        yield return new WaitForSecondsRealtime(HoldSeconds);
        SetKeys();
        yield return new WaitForSecondsRealtime(HoldSeconds);
    }

    public static IEnumerator GoToWithNativeFollow(Vector3 target, bool nativeMouseAim = false)
    {
        EnsureKeyboard();
        var follow = (FollowController)AccessTools.Field(typeof(CommandService), "_follow").GetValue(World.commandService);
        float duration = (float)AccessTools.Field(typeof(FollowController), "_followLongPressDuration").GetValue(follow);
        float finishAt = Time.realtimeSinceStartup + duration + HoldSeconds * 2;
        SetMousePosition(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
        _aimTarget = target;
        Camera.main.transform.LookAt(target);
        if (nativeMouseAim) AimWithNativeMouse(target);
        SetKeys(Key.F);
        while (Time.realtimeSinceStartup < finishAt)
        {
            Camera.main.transform.LookAt(target);
            if (nativeMouseAim) AimWithNativeMouse(target);
            SetMousePosition(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            yield return null;
        }
        Camera.main.transform.LookAt(target);
        if (nativeMouseAim) AimWithNativeMouse(target);
        SetKeys();
        yield return new WaitForSecondsRealtime(HoldSeconds);
        _aimTarget = null;
    }

    private static void AimWithNativeMouse(Vector3 target)
    {
        if (Mouse.current == null) _ownedMouse = (Mouse)CreateDevice("Mouse");
        AccessTools.Method(typeof(PlayerController), "LockCursor").Invoke(null, null);
        PlayerController player = World.playerController;
        Quaternion desired = Quaternion.LookRotation(target - Camera.main.transform.position);
        float yaw = (float)AccessTools.Field(typeof(PlayerController), "yaw").GetValue(player);
        float pitch = (float)AccessTools.Field(typeof(PlayerController), "pitch").GetValue(player);
        float sensitivity = (float)AccessTools.Field(typeof(PlayerController), "mouseSensitivity").GetValue(player);
        float desiredPitch = Mathf.DeltaAngle(0, desired.eulerAngles.x);
        Vector2 delta = new(Mathf.DeltaAngle(yaw, desired.eulerAngles.y) / sensitivity, (pitch - desiredPitch) / sensitivity);
        InputState.Change(Mouse.current, new MouseState { position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), delta = delta });
    }

    public static void ApplyNativeCameraAim()
    {
        if (_aimTarget.HasValue && Camera.main != null) Camera.main.transform.LookAt(_aimTarget.Value);
    }

    public static void SetKeys(params Key[] keys) => InputState.Change(Keyboard.current, new KeyboardState(keys));

    public static void EnsureKeyboard()
    {
        if (Keyboard.current != null) return;
        _ownedKeyboard = (Keyboard)CreateDevice("Keyboard");
    }

    public static void SetMouseButtons(ushort buttons)
    {
        if (Mouse.current == null) _ownedMouse = (Mouse)CreateDevice("Mouse");
        InputState.Change(Mouse.current, new MouseState { buttons = buttons });
    }

    public static void SetMousePosition(Vector2 position)
    {
        if (Mouse.current == null) _ownedMouse = (Mouse)CreateDevice("Mouse");
        InputState.Change(Mouse.current, new MouseState { position = position });
    }

    private static InputDevice CreateDevice(string deviceClass)
    {
        object manager = AccessTools.Field(typeof(InputSystem), "s_Manager").GetValue(null);
        var add = manager.GetType().GetMethods().Single(method => method.Name == "AddDevice" && method.GetParameters().Length == 5 && method.GetParameters()[1].ParameterType == typeof(bool));
        var description = new InputDeviceDescription { deviceClass = deviceClass, product = "Aircraft diagnostic " + deviceClass };
        return (InputDevice)add.Invoke(manager, new[] { (object)description, true, "AircraftDiagnostic" + deviceClass, 0, Enum.ToObject(add.GetParameters()[4].ParameterType, 0) });
    }

    public static void ReleaseKeyboard()
    {
        RemoveOwnedDevice(_ownedKeyboard);
        RemoveOwnedDevice(_ownedMouse);
        _ownedKeyboard = null;
        _ownedMouse = null;
        _aimTarget = null;
    }

    private static void RemoveOwnedDevice(InputDevice? device)
    {
        if (device == null) return;
        object manager = AccessTools.Field(typeof(InputSystem), "s_Manager").GetValue(null);
        AccessTools.Method(manager.GetType(), "RemoveDevice", new[] { typeof(InputDevice), typeof(bool) }).Invoke(manager, new object[] { device, false });
    }
}
