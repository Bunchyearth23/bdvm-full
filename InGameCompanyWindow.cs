using System;
using DV.Interaction.Inputs;
using UnityEngine;

namespace BDVM;

public sealed class InGameCompanyWindow : MonoBehaviour
{
    private const int WindowId = 0x445643;
    private Action? drawContents;
    private Action<string>? log;
    private Rect windowRect = new Rect(30f, 60f, 700f, 760f);
    private Vector2 scroll;
    private bool visible;
    private bool worldInputBlocked;

    public void Configure(Action contents, Action<string> logger)
    {
        drawContents = contents ?? throw new ArgumentNullException(nameof(contents));
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

        windowRect.width = Math.Min(700f, Math.Max(420f, Screen.width - 40f));
        windowRect.height = Math.Min(760f, Math.Max(320f, Screen.height - 80f));
        windowRect.x = Mathf.Clamp(windowRect.x, 0f, Math.Max(0f, Screen.width - windowRect.width));
        windowRect.y = Mathf.Clamp(windowRect.y, 0f, Math.Max(0f, Screen.height - windowRect.height));
        windowRect = GUI.Window(WindowId, windowRect, DrawWindow, "BDVM 2.2.0");
    }

    private void DrawWindow(int id)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Interface économique — visible uniquement en mouse mode");
        if (GUILayout.Button("Fermer", GUILayout.Width(90f))) SetVisible(false, "close-button");
        GUILayout.EndHorizontal();
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
}
