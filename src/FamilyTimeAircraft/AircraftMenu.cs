using System;
using FamilyTimeAircraft.Core;
using GAT;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FamilyTimeAircraft;

public sealed class AircraftMenu : MonoBehaviour
{
    private const PlayerController.ReasonToShowCursor CursorReason = (PlayerController.ReasonToShowCursor)10001;
    private AircraftRuntime _runtime = null!;
    private GameObject? _canvas;
    private GameObject? _panel;
    private TMP_Text? _status;
    private Aircraft? _selected;
    private bool _choosingCrash;
    private float _nextRefresh;
    private float _nextControl;
    private float _throttle;
    private int _manualAircraftId;
    private int _lastBoardInputFrame = -1;
    private const float BoardingReach = 10;
    public bool CapturesNativeCommands => IsOpen || _choosingCrash;
    public bool IsOpen => _canvas != null && _canvas.activeSelf;

    public void Initialize(AircraftRuntime runtime) => _runtime = runtime;

    private void Update()
    {
        if (!AircraftRuntime.IsGameReady) { Close(); return; }
        Keyboard? keyboard = Keyboard.current;
        if (keyboard != null && _runtime.Controls.Menu.Value != Key.None && keyboard[_runtime.Controls.Menu.Value].wasPressedThisFrame) Toggle();
        if (keyboard != null && _runtime.Controls.Board.Value != Key.None && keyboard[_runtime.Controls.Board.Value].wasPressedThisFrame) TryToggleAircraftSeat();
        if (keyboard != null && _runtime.Controls.Board.Value != Key.None && keyboard[_runtime.Controls.Board.Value].wasReleasedThisFrame) AircraftBoardingInputPatch.ReleaseGesture();
        if (_choosingCrash) PollCrashTarget();
        PollPilotControls();
        if (!IsOpen || Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 1;
        Refresh();
    }

    public bool TryToggleAircraftSeat()
    {
        if (_lastBoardInputFrame == Time.frameCount) return true;
        if (!AircraftRuntime.IsGameReady || IsOpen || _choosingCrash || PlayerController.IsTypingInInputField || PlayerController.showCursor.value) return false;
        Aircraft? seated = AircraftInputState.LocalAircraft();
        if (seated != null)
        {
            _lastBoardInputFrame = Time.frameCount;
            Send(AircraftCommandKind.ExitPlayer, seated.Id);
            return true;
        }
        if (AircraftInputState.IsLocalPassenger()) return true;
        Aircraft? plane = LookedAtAircraft();
        if (plane == null) return false;
        _lastBoardInputFrame = Time.frameCount;
        Send(AircraftCommandKind.BoardPlayer, plane.Id, AircraftRules.AutomaticBoardingSeat);
        return true;
    }

    private Aircraft? LookedAtAircraft()
    {
        Camera? camera = Camera.main;
        if (camera == null) return null;
        Ray ray = new(camera.transform.position, camera.transform.forward);
        Aircraft? nearest = null;
        float distance = BoardingReach;
        foreach (Aircraft plane in _runtime.World.Aircrafts)
        {
            if (Vector3.Distance(World.playerController.CachedInstancePosition, GameFlightEnvironment.ToUnity(plane.Position)) > BoardingReach) continue;
            float? hitDistance = AircraftRayDistance(plane, ray);
            if (!hitDistance.HasValue || hitDistance.Value >= distance) continue;
            nearest = plane;
            distance = hitDistance.Value;
        }
        return nearest;
    }

    private float? AircraftRayDistance(Aircraft plane, Ray ray)
    {
        Matrix4x4 shape = World.buildPartManager.BuildPartTemplates[_runtime.Blueprints.AircraftIndex].collisionShapes[0];
        Matrix4x4 transform = Matrix4x4.TRS(GameFlightEnvironment.ToUnity(plane.Position), GameFlightEnvironment.ToUnity(plane.Rotation), Vector3.one) * shape;
        Matrix4x4 inverse = transform.inverse;
        Ray localRay = new(inverse.MultiplyPoint3x4(ray.origin), inverse.MultiplyVector(ray.direction));
        if (!new Bounds(Vector3.zero, Vector3.one).IntersectRay(localRay, out float distance)) return null;
        return Vector3.Distance(ray.origin, transform.MultiplyPoint3x4(localRay.GetPoint(distance)));
    }

    private void Toggle()
    {
        if (IsOpen) { Close(); return; }
        if (_canvas == null) CreateCanvas();
        _canvas!.SetActive(true);
        PlayerController.showCursor.Add(CursorReason);
        Refresh();
    }

    private void Close()
    {
        if (_canvas != null) _canvas.SetActive(false);
        PlayerController.showCursor.Remove(CursorReason);
    }

    private void CreateCanvas()
    {
        _canvas = new GameObject("Aircraft menu", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _canvas.transform.SetParent(transform, false);
        Canvas canvas = _canvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        CanvasScaler scaler = _canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        CreatePanel();
    }

    private void CreatePanel()
    {
        var outer = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        _panel = outer;
        _panel.transform.SetParent(_canvas!.transform, false);
        RectTransform rect = _panel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(1, 1);
        rect.anchoredPosition = new Vector2(-20, -20);
        rect.sizeDelta = new Vector2(350, 680);
        _panel.GetComponent<Image>().color = new Color(0.08f, 0.11f, 0.14f, 0.95f);
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(outer.transform, false);
        Stretch(viewport.GetComponent<RectTransform>());
        viewport.GetComponent<Mask>().showMaskGraphic = false;
        _panel = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        _panel.transform.SetParent(viewport.transform, false);
        RectTransform content = _panel.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1);
        content.sizeDelta = Vector2.zero;
        _panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        ScrollRect scroll = outer.GetComponent<ScrollRect>();
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = content;
        scroll.horizontal = false;
        scroll.scrollSensitivity = 30;
        VerticalLayoutGroup layout = _panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 5;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
    }

    private void Refresh()
    {
        if (_panel == null) return;
        foreach (Transform child in _panel.transform) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        _selected = NearestAircraft();
        AddText("AIRCRAFT · " + _runtime.Controls.Menu.Value, 30);
        AddText("Build aircraft and airfields in the native building menu. Aircraft occupies slot 4.", 48);
        if (_selected == null) { AddText("No aircraft nearby.", 35); return; }
        int id = _selected.Id;
        _status = AddText("Aircraft " + id + " · " + PhaseLabel(_selected.Phase) + " · " + _selected.Seats.Count + "/9", 35);
        AddButton("Board as pilot", () => Send(AircraftCommandKind.BoardPlayer, id, 0));
        AddButton("Board as passenger", () => Send(AircraftCommandKind.BoardPlayer, id, -1));
        AddButton("Assign a child pilot", () => Send(AircraftCommandKind.AssignChildPilot, id));
        AddButton("Order children to board", () => Send(AircraftCommandKind.BoardChildren, id, 8));
        AddButton("Disembark / unload children", () => Send(AircraftCommandKind.Disembark, id));
        foreach (Airfield airfield in _runtime.World.Airfields)
        {
            int destination = airfield.Id;
            AddButton("Fly to airfield " + destination, () => Send(AircraftCommandKind.FlightToAirfield, id, destination));
        }
        AddButton("Crash here: choose target", () => { _choosingCrash = true; Close(); _runtime.SetNotice("Aim at the crash target and right-click. Escape cancels."); });
        AddButton("Repair wreck · 8 logs, 4 stones", () => Send(AircraftCommandKind.Repair, id));
        AddText(ControlHint(), 90);
        AddText("Hold F, aim at a destination and release. Aircraft passengers parachute there; a child pilot lands at the nearest airfield before walking there.", 90);
        AddText(_runtime.Notice, 48);
    }

    private Aircraft? NearestAircraft()
    {
        Aircraft? nearest = null;
        float distance = 150;
        Vector3 player = World.playerController.CachedInstancePosition;
        foreach (Aircraft plane in _runtime.World.Aircrafts)
        {
            float candidate = Vector3.Distance(player, GameFlightEnvironment.ToUnity(plane.Position));
            if (candidate >= distance) continue;
            nearest = plane;
            distance = candidate;
        }
        return nearest;
    }

    private string ControlHint()
    {
        AircraftControls keys = _runtime.Controls;
        return $"{keys.Board.Value}: enter / exit; deploy a parachute in flight. {keys.PitchUp.Value}/{keys.AlternatePitchUp.Value}: nose up; {keys.PitchDown.Value}/{keys.AlternatePitchDown.Value}: nose down. "
            + $"{keys.RollLeft.Value}/{keys.RollRight.Value} or arrows: roll, "
            + $"{keys.YawLeft.Value}/{keys.YawRight.Value} yaw, {keys.IncreaseThrottle.Value} throttle up, "
            + $"{keys.DecreaseThrottle.Value} throttle cut + brake, {keys.Brake.Value} brake. Rebind in BepInEx config.";
    }

    private static string PhaseLabel(FlightPhase phase) => phase switch
    {
        FlightPhase.Parked => "Parked", FlightPhase.Taxiing => "Taxiing", FlightPhase.TakingOff => "Taking off",
        FlightPhase.Cruising => "Cruising", FlightPhase.Approaching => "Approaching", FlightPhase.Landing => "Landing",
        FlightPhase.Manual => "Manual flight", FlightPhase.CrashApproach => "Crash approach", FlightPhase.Wrecked => "Wrecked",
        FlightPhase.EmergencyLanding => "Emergency landing", FlightPhase.Unpiloted => "Unpiloted descent",
        _ => throw new InvalidOperationException("Unknown aircraft flight phase.")
    };

    private void Send(AircraftCommandKind kind, int id, int target = 0) => _runtime.Dispatch(new AircraftCommand(kind, id, target));

    private void PollCrashTarget()
    {
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true) { _choosingCrash = false; return; }
        if (Mouse.current?.rightButton.wasPressedThisFrame != true || Camera.main == null || _selected == null) return;
        if (!World.Raycast(Camera.main.transform.position, Camera.main.transform.forward, 1500, out var hit)) return;
        _runtime.Dispatch(new AircraftCommand(AircraftCommandKind.CrashAt, _selected.Id, position: GameFlightEnvironment.ToCore(hit.point)));
        _choosingCrash = false;
    }

    private void PollPilotControls()
    {
        if (IsOpen || PlayerController.IsTypingInInputField || PlayerController.showCursor.value || Time.unscaledTime < _nextControl || Keyboard.current == null) return;
        Aircraft? plane = LocalPilotAircraft();
        if (plane == null) { _manualAircraftId = 0; return; }
        if (_manualAircraftId != plane.Id)
        {
            _manualAircraftId = plane.Id;
            _throttle = plane.CurrentInput.Throttle;
        }
        Keyboard keys = Keyboard.current;
        _nextControl = Time.unscaledTime + 0.05f;
        bool brake = IsBrakePressed(keys);
        _throttle = brake ? 0 : Mathf.Clamp01(_throttle + AircraftControls.Axis(keys, _runtime.Controls.IncreaseThrottle, _runtime.Controls.DecreaseThrottle) * 0.05f);
        FlightInput input = new(_throttle, AircraftControls.Axis(keys, _runtime.Controls.PitchUp, _runtime.Controls.AlternatePitchUp, _runtime.Controls.PitchDown, _runtime.Controls.AlternatePitchDown),
            AircraftControls.Axis(keys, _runtime.Controls.YawRight, _runtime.Controls.YawLeft),
            AircraftControls.Axis(keys, _runtime.Controls.RollRight, _runtime.Controls.AlternateRollRight, _runtime.Controls.RollLeft, _runtime.Controls.AlternateRollLeft),
            brake);
        _runtime.Dispatch(new AircraftCommand(AircraftCommandKind.ManualControl, plane.Id, input: input));
    }

    private bool IsBrakePressed(Keyboard keyboard) => AircraftControls.Pressed(keyboard, _runtime.Controls.Brake)
        || AircraftControls.Pressed(keyboard, _runtime.Controls.DecreaseThrottle);

    private Aircraft? LocalPilotAircraft()
    {
        foreach (Aircraft plane in _runtime.World.Aircrafts)
            if (plane.Seats.TryGetValue(0, out Occupant occupant) && occupant.Kind == OccupantKind.Player && occupant.Id == _runtime.LocalPlayerId) return plane;
        return null;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private TMP_Text AddText(string text, float height)
    {
        var entity = new GameObject("Text", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
        entity.transform.SetParent(_panel!.transform, false);
        entity.GetComponent<LayoutElement>().preferredHeight = height;
        TMP_Text label = entity.GetComponent<TMP_Text>();
        label.text = text;
        label.fontSize = 18;
        label.color = Color.white;
        return label;
    }

    private void AddButton(string text, Action callback)
    {
        var entity = new GameObject("Button", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(Button));
        entity.transform.SetParent(_panel!.transform, false);
        entity.GetComponent<LayoutElement>().preferredHeight = 32;
        entity.GetComponent<Image>().color = new Color(0.22f, 0.33f, 0.4f);
        entity.GetComponent<Button>().onClick.AddListener(() => callback());
        var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(entity.transform, false);
        RectTransform rect = label.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        TMP_Text content = label.GetComponent<TMP_Text>();
        content.text = text;
        content.fontSize = 17;
        content.color = Color.white;
        content.alignment = TextAlignmentOptions.Center;
    }

    private void OnDestroy() => Close();
}
