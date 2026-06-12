namespace KeyboardWtf.Services;

using System.Runtime.InteropServices;
using System.Windows.Forms;

public sealed class HotkeyService : IMessageFilter, IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly Dictionary<int, (string Name, Action Handler)> _handlers = new();
    private int _nextId = 100;
    private bool _disposed;

    public event Action<string, string> RegistrationFailed;

    public void RegisterDefaults(HotkeySettings settings, CommandRegistry commands)
    {
        UnregisterAll();
        Application.AddMessageFilter(this);

        var hotkeys = settings ?? HotkeySettings.Defaults();
        Register("SmartWriting", hotkeys.PushToTalk, commands.ToggleSmartMode);
        Register("Dictation", hotkeys.Dictation, commands.ToggleDictation);
        Register("Jarvis", hotkeys.Jarvis, commands.ToggleJarvisMode);
        Register("Cancel", hotkeys.Cancel, commands.CancelCurrent);
        Register("OpenSettings", hotkeys.OpenSettings, commands.OpenSettings);
    }

    public static bool HasDuplicates(HotkeySettings settings, out string duplicate)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in (settings ?? HotkeySettings.Defaults()).ToDictionary())
        {
            var normalized = Normalize(pair.Value);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;
            if (!seen.Add(normalized))
            {
                duplicate = pair.Value;
                return true;
            }
        }

        duplicate = null;
        return false;
    }

    private void Register(string name, string hotkey, Action handler)
    {
        if (!TryParse(hotkey, out var modifiers, out var key))
        {
            RegistrationFailed?.Invoke(name, $"Invalid hotkey: {hotkey}");
            return;
        }

        var id = _nextId++;
        if (!RegisterHotKey(IntPtr.Zero, id, modifiers, (uint)key))
        {
            RegistrationFailed?.Invoke(name, $"{hotkey} is already in use by Windows or another app.");
            return;
        }

        _handlers[id] = (name, handler);
        AppLog.Info($"Registered hotkey {name}: {hotkey}");
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WmHotkey)
            return false;

        var id = m.WParam.ToInt32();
        if (_handlers.TryGetValue(id, out var entry))
        {
            try { entry.Handler(); }
            catch (Exception ex) { AppLog.Error(ex, $"Hotkey handler failed: {entry.Name}"); }
            return true;
        }

        return false;
    }

    private void UnregisterAll()
    {
        foreach (var id in _handlers.Keys.ToArray())
            UnregisterHotKey(IntPtr.Zero, id);
        _handlers.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Application.RemoveMessageFilter(this);
        UnregisterAll();
    }

    private static string Normalize(string hotkey) =>
        string.Join("+", (hotkey ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Equals("Control", StringComparison.OrdinalIgnoreCase) ? "Ctrl" : p)
            .Select(p => p.ToUpperInvariant()));

    private static bool TryParse(string hotkey, out uint modifiers, out Keys key)
    {
        modifiers = 0;
        key = Keys.None;

        foreach (var raw in (hotkey ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || raw.Equals("Control", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModControl;
            else if (raw.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModAlt;
            else if (raw.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModShift;
            else if (raw.Equals("Win", StringComparison.OrdinalIgnoreCase) || raw.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModWin;
            else if (raw == ",")
                key = Keys.Oemcomma;
            else if (raw.Equals("Space", StringComparison.OrdinalIgnoreCase))
                key = Keys.Space;
            else if (Enum.TryParse<Keys>(raw, true, out var parsed))
                key = parsed;
        }

        return key != Keys.None;
    }

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
