using System;
using DV.Interaction.Inputs;
using UnityEngine;

namespace BDVM;

public sealed class InGameCompanyWindow : MonoBehaviour
{
    private const int WindowId = 0x445643;
    private Action? drawContents;
    private Func<string>? getStatus;
    private Action<string>? log;
    private Rect windowRect = new Rect(30f, 60f, 1100f, 760f);
    private GUIStyle? opaqueWindowStyle;
    private Texture2D? opaqueWindowBackground;
    private Vector2 scroll;
    private bool visible;
    private bool worldInputBlocked;

    public void Configure(Action contents, Func<string> statusProvider, Action<string> logger)
    {
        drawContents = contents ?? throw new ArgumentNullException(nameof(contents));
        getStatus = statusProvider ?? throw new ArgumentNullException(nameof(statusProvider));
        log = logger;
    }

    private void Update()
    {
        var mouseMode = IsMouseMode();
        if (!mouseMode && worldInputBlocked) SetWorldInputBlocked(false, "mouse-mode-ended");
        else if (mouseMode && visible && !worldInputBlocked) SetWorldInputBlocked(true, "mouse-mode-resumed");
        if (mouseMode && Input.GetKeyDown(KeyCode.F7)) SetVisible(!visible, "hotkey");
    }

    private void OnGUI()
    {
        if (!IsMouseMode()) return;
        if (!visible)
        {
            var launcher = new Rect(Math.Max(12f, Screen.width - 154f), 12f, 142f, 34f);
            if (GUI.Button(launcher, "BDVM [F7]")) SetVisible(true, "button");
            return;
        }

        windowRect.width = Math.Min(Screen.width - 40f, Math.Max(760f, Screen.width * 0.82f));
        windowRect.height = Math.Min(760f, Math.Max(320f, Screen.height - 80f));
        windowRect.x = Mathf.Clamp(windowRect.x, 0f, Math.Max(0f, Screen.width - windowRect.width));
        windowRect.y = Mathf.Clamp(windowRect.y, 0f, Math.Max(0f, Screen.height - windowRect.height));
        EnsureOpaqueWindowStyle();
        windowRect = GUI.Window(WindowId, windowRect, DrawWindow, "BDVM 0.3.0 beta", opaqueWindowStyle);
    }

    private void EnsureOpaqueWindowStyle()
    {
        if (opaqueWindowStyle != null) return;
        opaqueWindowBackground = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "BDVM Opaque Window Background",
            hideFlags = HideFlags.HideAndDontSave
        };
        opaqueWindowBackground.SetPixel(0, 0, new Color(0.075f, 0.085f, 0.095f, 1f));
        opaqueWindowBackground.Apply(false, true);

        opaqueWindowStyle = new GUIStyle(GUI.skin.window);
        opaqueWindowStyle.normal.background = opaqueWindowBackground;
        opaqueWindowStyle.hover.background = opaqueWindowBackground;
        opaqueWindowStyle.active.background = opaqueWindowBackground;
        opaqueWindowStyle.focused.background = opaqueWindowBackground;
        opaqueWindowStyle.onNormal.background = opaqueWindowBackground;
        opaqueWindowStyle.onHover.background = opaqueWindowBackground;
        opaqueWindowStyle.onActive.background = opaqueWindowBackground;
        opaqueWindowStyle.onFocused.background = opaqueWindowBackground;
        opaqueWindowStyle.border = new RectOffset(0, 0, 0, 0);
    }

    private void DrawWindow(int id)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Economic interface — visible only in mouse mode");
        if (GUILayout.Button("Close", GUILayout.Width(90f))) SetVisible(false, "close-button");
        GUILayout.EndHorizontal();
        DrawStatusBanner();
        scroll = GUILayout.BeginScrollView(scroll);
        try { drawContents?.Invoke(); }
        catch (Exception exception)
        {
            GUILayout.Label("Interface error: " + exception.Message);
            log?.Invoke("[correlation=ingame-ui] [event=ui-error] " + exception);
        }
        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0f, 0f, windowRect.width - 100f, 28f));
    }

    private void DrawStatusBanner()
    {
        var message = getStatus?.Invoke() ?? "";
        if (string.IsNullOrWhiteSpace(message)) return;

        var failure = message.IndexOf("refused", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0;
        var previous = GUI.color;
        GUI.color = failure
            ? new Color(1f, 0.58f, 0.58f, 1f)
            : new Color(0.62f, 1f, 0.68f, 1f);
        GUILayout.Box((failure ? "Action refused: " : "Last action: ") + message, GUILayout.ExpandWidth(true));
        GUI.color = previous;
    }

    private void SetVisible(bool value, string source)
    {
        if (visible == value) return;
        visible = value;
        SetWorldInputBlocked(visible && IsMouseMode(), source);
        log?.Invoke("[correlation=ingame-ui] [event=ui-visibility] visible=" + visible + ", source=" + source);
    }

    private void SetWorldInputBlocked(bool blocked, string source)
    {
        if (worldInputBlocked == blocked) return;
        InputManager.SetKeyboardAndMouseEnabled(this, !blocked);
        worldInputBlocked = blocked;
        log?.Invoke("[correlation=ingame-ui] [event=ui-world-input] blocked=" + blocked + ", source=" + source);
    }

    private static bool IsMouseMode() => Cursor.visible && Cursor.lockState != CursorLockMode.Locked;

    private void OnDisable()
    {
        if (worldInputBlocked) SetWorldInputBlocked(false, "component-disabled");
        visible = false;
    }

    private void OnDestroy()
    {
        if (opaqueWindowBackground != null) Destroy(opaqueWindowBackground);
        opaqueWindowBackground = null;
        opaqueWindowStyle = null;
    }
}
