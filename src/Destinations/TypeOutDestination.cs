namespace KeyboardWtf.Destinations;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using KeyboardWtf.Helpers;
using KeyboardWtf.Models;

[SupportedOSPlatform("windows")]
public class TypeOutDestination : IDestination
{
    public string Name => "Type Out";
    public string Description => "Type voice directly into the active window";
    public bool IsAvailable => true;
    public DestinationCategory Category => DestinationCategory.Raw;
    public string AiPrompt => null;

    private const int PreTypeDelayMs = 200;

    public async Task<bool> SendAsync(string text)
    {
        try
        {
            ClipboardHelper.SetText(text);
            await Task.Delay(PreTypeDelayMs);

            var pasted = SendPasteShortcut();
            AppLog.Info(pasted
                ? $"Pasted {text.Length} chars into active window"
                : "TypeOut paste shortcut failed; text remains on clipboard");
            return pasted;
        }
        catch (Exception ex)
        {
            AppLog.Error($"TypeOut failed: {ex.Message}");
            return false;
        }
    }

    private static bool SendPasteShortcut()
    {
        var inputs = new[]
        {
            KeyboardInput(VkControl, 0),
            KeyboardInput((ushort)Keys.V, 0),
            KeyboardInput((ushort)Keys.V, KeyeventfKeyup),
            KeyboardInput(VkControl, KeyeventfKeyup),
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            AppLog.Warning($"TypeOut: sent {sent}/{inputs.Length} paste input events");
            return false;
        }

        return true;
    }

    private static INPUT KeyboardInput(ushort key, uint flags) =>
        new()
        {
            type = InputKeyboard,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = key,
                    dwFlags = flags,
                    dwExtraInfo = GetMessageExtraInfo()
                }
            }
        };

    // --- Win32 P/Invoke ---

    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const ushort VkControl = 0x11;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetMessageExtraInfo();
}
