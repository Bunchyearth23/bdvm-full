// Minimal drawing surface for offline controller tests; no Unity/runtime proof.
namespace UnityEngine
{
    public class GUILayoutOption {}
    public class GUIStyle { public GUIStyle() {} public GUIStyle(GUIStyle other) {} public int fontSize; public bool wordWrap; }
    public class GUISkin { public GUIStyle label = new GUIStyle(); public GUIStyle box = new GUIStyle(); }
    public static class GUI { public static bool enabled = true; public static GUISkin skin = new GUISkin(); }
    public static class GUILayout
    {
        public static readonly System.Collections.Generic.List<string> Text = new System.Collections.Generic.List<string>();
        public static string Click = "";
        public static GUILayoutOption Width(float width) => new GUILayoutOption();
        public static void BeginHorizontal() {} public static void EndHorizontal() {}
        public static void BeginVertical(GUIStyle style) {} public static void EndVertical() {}
        public static void Label(string text, params GUILayoutOption[] options) { Text.Add(text); }
        public static void Label(string text, GUIStyle? style, params GUILayoutOption[] options) { Text.Add(text); }
        public static void Box(string text, GUIStyle? style) { Text.Add(text); }
        public static bool Button(string text, params GUILayoutOption[] options) { Text.Add(text); if (GUI.enabled && Click == text) { Click = ""; return true; } return false; }
        public static string TextField(string value, int maximum) => value;
        public static bool Toggle(bool value, string text) => value;
        public static int SelectionGrid(int selected, string[] names, int columns) => selected;
    }
}