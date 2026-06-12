namespace KeyboardWtf.Services;

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using KeyboardWtf.Helpers;

public sealed class JarvisAutomationService : IDisposable
{
    private const int SwHide = 0;
    private const int SwRestore = 9;
    private const int SwMinimize = 6;
    private const int SwMaximize = 3;
    private const uint WmClose = 0x0010;
    private const byte VkVolumeMute = 0xAD;
    private const byte VkVolumeDown = 0xAE;
    private const byte VkVolumeUp = 0xAF;
    private const byte VkLeftWindows = 0x5B;
    private const byte VkD = 0x44;
    private const uint KeyeventfKeyup = 0x0002;

    private readonly NotificationService _notifications;
    private readonly SettingsService _settings;
    private readonly JarvisActionHistoryService _history;
    private readonly Action _openSettings;
    private readonly object _pendingLock = new();
    private readonly Dictionary<string, System.Threading.Timer> _timers = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _todosPath = Path.Combine(SettingsService.AppDataDir, "jarvis-todos.json");
    private PendingJarvisAction _pending;
    private bool _executingApprovedAction;
    private bool _disposed;

    public JarvisAutomationService(
        NotificationService notifications,
        SettingsService settings,
        JarvisActionHistoryService history,
        Action openSettings)
    {
        _notifications = notifications;
        _settings = settings;
        _history = history;
        _openSettings = openSettings;
    }

    public async Task<Dictionary<string, object>> ExecuteAsync(
        string toolName,
        JsonElement args,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        toolName = (toolName ?? "").Trim();
        if (ShouldRequestPermission(toolName, args))
            return RequestRoutinePermission(toolName, args);

        return await ExecuteCoreAsync(toolName, args, token);
    }

    public async Task<Dictionary<string, object>> ExecuteApprovedBridgeAsync(
        string toolName,
        JsonElement args,
        CancellationToken token)
    {
        _executingApprovedAction = true;
        try
        {
            return await ExecuteCoreAsync(toolName, args, token);
        }
        finally
        {
            _executingApprovedAction = false;
        }
    }

    private async Task<Dictionary<string, object>> ExecuteCoreAsync(
        string toolName,
        JsonElement args,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return toolName switch
        {
            "open_app" => OpenApp(GetString(args, "app_name")),
            "open_url" => OpenUrl(GetString(args, "url")),
            "open_folder" => OpenFolder(GetString(args, "folder")),
            "open_path" => OpenPath(GetString(args, "path")),
            "window_action" => WindowAction(GetString(args, "action"), GetString(args, "app_name")),
            "browser_action" => BrowserAction(GetString(args, "action")),
            "web_search" => WebSearch(GetString(args, "query"), GetString(args, "engine")),
            "get_desktop_context" => GetDesktopContext(),
            "get_clipboard_text" => GetClipboardText(),
            "get_selected_text" => await GetSelectedTextAsync(token),
            "replace_selected_text" => await ReplaceSelectedTextAsync(GetString(args, "text"), token),
            "type_text" => await TypeTextAsync(GetString(args, "text"), token),
            "press_key" => PressKey(GetString(args, "key")),
            "search_files" => await SearchFilesAsync(
                GetString(args, "query"),
                GetString(args, "location"),
                token),
            "save_note" => SaveNote(GetString(args, "text"), GetString(args, "title")),
            "add_todo" => AddTodo(GetString(args, "text")),
            "list_todos" => ListTodos(),
            "complete_todo" => CompleteTodo(GetString(args, "query")),
            "set_timer" => SetTimer(GetInt(args, "minutes"), GetString(args, "label")),
            "system_control" => SystemControl(GetString(args, "action"), GetInt(args, "value")),
            "system_status" => SystemStatus(),
            "play_media" => PlayMedia(GetString(args, "query"), GetString(args, "service")),
            "open_service_page" => OpenServicePage(
                GetString(args, "service"),
                GetString(args, "page"),
                GetString(args, "query")),
            "open_camera" => OpenCamera(),
            "take_screenshot" => TakeScreenshot(),
            "open_gmail_draft" => OpenGmailDraft(
                GetString(args, "recipient"),
                GetString(args, "subject"),
                GetString(args, "body")),
            "prepare_whatsapp_message" => PrepareWhatsAppMessage(
                GetString(args, "message"),
                GetString(args, "contact_name"),
                GetString(args, "phone_number")),
            "copy_text" => CopyText(GetString(args, "text")),
            "create_workflow" => CreateWorkflow(args),
            "list_workflows" => ListWorkflows(),
            "run_workflow" => await RunWorkflowAsync(GetString(args, "name"), token),
            "delete_workflow" => DeleteWorkflow(GetString(args, "name")),
            "request_sensitive_action" => RequestSensitiveAction(
                GetString(args, "action"),
                GetString(args, "target")),
            "confirm_sensitive_action" => await ConfirmPendingActionAsync(token),
            "cancel_sensitive_action" => CancelPendingAction(),
            _ => Result(false, $"{toolName} is not a supported desktop action yet.", supported: false),
        };
    }

    private bool ShouldRequestPermission(string toolName, JsonElement args)
    {
        if (_executingApprovedAction
            || toolName is "request_sensitive_action" or "confirm_sensitive_action" or "cancel_sensitive_action")
            return false;

        if (toolName is "get_desktop_context" or "get_clipboard_text" or "get_selected_text"
            or "search_files" or "list_todos" or "list_workflows" or "system_status")
            return false;

        if (toolName == "window_action"
            && NormalizeWords(GetString(args, "action")) is "list" or "list windows")
            return false;

        var sideEffecting = toolName is
            "open_app" or "open_url" or "open_folder" or "open_path"
            or "window_action" or "browser_action" or "web_search"
            or "replace_selected_text" or "type_text" or "press_key"
            or "save_note" or "add_todo" or "complete_todo" or "set_timer"
            or "system_control" or "play_media" or "open_service_page" or "open_camera" or "take_screenshot"
            or "open_gmail_draft" or "prepare_whatsapp_message" or "copy_text"
            or "create_workflow" or "run_workflow" or "delete_workflow";
        if (!sideEffecting)
            return false;

        var appName = toolName == "open_app" ? NormalizeWords(GetString(args, "app_name")) : "";
        // Camera and screen capture always ask, even in auto-execute mode.
        if (toolName is "open_camera" or "take_screenshot" || appName is "camera" or "windows camera")
            return true;

        return _settings.Current.JarvisPermissionMode == JarvisPermissionMode.AlwaysAsk;
    }

    private Dictionary<string, object> RequestRoutinePermission(string toolName, JsonElement args)
    {
        var description = DescribeRoutineAction(toolName, args);
        lock (_pendingLock)
        {
            _pending = new PendingJarvisAction
            {
                ToolName = toolName,
                Args = args.ValueKind == JsonValueKind.Undefined
                    ? JsonSerializer.SerializeToElement(new { })
                    : args.Clone(),
                Action = toolName,
                Description = description,
            };
        }

        Models.KeyboardWtfState.SetUi(Models.VoiceUiPhase.Thinking, "Permission required", description);
        _history.Add("permission_requested", description, true, true);
        return Result(
            true,
            $"{description}. Ask the user to say confirm or cancel.",
            confirmationRequired: true,
            extras: new() { ["description"] = description, ["permission_mode"] = "AlwaysAsk" });
    }

    private static string DescribeRoutineAction(string toolName, JsonElement args) => toolName switch
    {
        "open_app" => $"Open {GetString(args, "app_name")}",
        "open_url" => $"Open {GetString(args, "url")}",
        "open_folder" => $"Open the {GetString(args, "folder")} folder",
        "open_path" => $"Open {GetString(args, "path")}",
        "window_action" => $"{GetString(args, "action")} the requested window",
        "browser_action" => $"{GetString(args, "action")} in the active browser",
        "web_search" => $"Search for {GetString(args, "query")}",
        "replace_selected_text" => "Replace the selected text",
        "type_text" => "Type text into the active field",
        "press_key" => $"Press {GetString(args, "key")}",
        "save_note" => "Save a local note",
        "add_todo" => "Add a local to-do",
        "complete_todo" => "Complete a local to-do",
        "set_timer" => "Start a timer",
        "system_control" => $"{GetString(args, "action")}",
        "play_media" => $"Open {GetString(args, "query")} on {GetString(args, "service")}",
        "open_service_page" => $"Open {GetString(args, "page")} on {GetString(args, "service")}",
        "open_camera" => "Open the Windows Camera app",
        "take_screenshot" => "Capture and save the current screen",
        "open_gmail_draft" => $"Prepare a Gmail draft for {GetString(args, "recipient")}",
        "prepare_whatsapp_message" => "Prepare a WhatsApp message",
        "copy_text" => "Copy prepared text to the clipboard",
        "create_workflow" => "Save a Jarvis workflow",
        "run_workflow" => $"Run the {GetString(args, "name")} workflow",
        "delete_workflow" => $"Delete the {GetString(args, "name")} workflow",
        _ => $"Run {toolName.Replace('_', ' ')}",
    };

    public void ObserveUserTranscript(string text)
    {
        PendingJarvisAction pending;
        lock (_pendingLock)
            pending = _pending;
        if (pending == null)
            return;

        var normalized = NormalizePhraseText(text);
        if (ContainsPhrase(normalized, "cancel", "no", "nope", "do not", "don't", "stop", "never mind"))
        {
            lock (_pendingLock)
                _pending = null;
            _history.Add("confirmation_cancelled", pending.Description, true);
            return;
        }

        if (normalized.Length <= 80
            && ContainsPhrase(normalized, "yes", "yeah", "yep", "sure", "confirm", "go ahead", "proceed", "do it", "okay do it", "ok do it"))
        {
            lock (_pendingLock)
            {
                if (_pending?.Id == pending.Id)
                    _pending.ConfirmedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    public object PendingSnapshot()
    {
        lock (_pendingLock)
        {
            if (_pending == null)
                return null;
            return new
            {
                _pending.Id,
                _pending.Action,
                _pending.Target,
                _pending.Description,
                _pending.ToolName,
                _pending.IsSensitive,
                _pending.RequestedAt,
                _pending.ConfirmedAt,
            };
        }
    }

    private Dictionary<string, object> OpenApp(string appName)
    {
        var name = NormalizeWords(appName);
        if (string.IsNullOrWhiteSpace(name))
            return Result(false, "Which app should I open?", needsClarification: true);

        var webApps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["gmail"] = "https://mail.google.com/",
            ["google drive"] = "https://drive.google.com/",
            ["drive"] = "https://drive.google.com/",
            ["google calendar"] = "https://calendar.google.com/",
            ["calendar"] = "https://calendar.google.com/",
            ["youtube"] = "https://www.youtube.com/",
            ["github"] = "https://github.com/",
            ["devpost"] = "https://devpost.com/",
            ["whatsapp"] = "https://web.whatsapp.com/",
        };
        if (webApps.TryGetValue(name, out var webUrl))
            return OpenUrl(webUrl);

        if (name is "settings" or "keyboard settings" or "keyboard.wtf settings")
        {
            _openSettings();
            return Logged("open_app", "keyboard.wtf settings", true, "Opened keyboard.wtf settings.");
        }
        if (name is "apple music")
        {
            if (TryStartStartApp("Apple Music")
                || TryStart("iTunes.exe"))
                return Logged("open_app", name, true, "Opened Apple Music.");

            var result = OpenUrl("https://music.apple.com/");
            result["message"] = "Apple Music is not installed here, so I opened Apple Music on the web.";
            return result;
        }
        if (name is "camera" or "windows camera")
            return OpenCamera();
        if (name is "photos" or "microsoft photos")
            return TryStart("ms-photos:")
                ? Logged("open_app", name, true, "Opened Photos.")
                : Logged("open_app", name, false, "Microsoft Photos could not be opened.", supported: false);
        if (name is "clock" or "alarms" or "alarms and clock")
            return TryStart("ms-clock:")
                ? Logged("open_app", name, true, "Opened Clock.")
                : Logged("open_app", name, false, "Windows Clock could not be opened.", supported: false);
        if (name is "microsoft store" or "store")
            return TryStart("ms-windows-store:")
                ? Logged("open_app", name, true, "Opened Microsoft Store.")
                : Logged("open_app", name, false, "Microsoft Store could not be opened.", supported: false);

        var candidates = name switch
        {
            "notepad" => new[] { "notepad.exe" },
            "calculator" or "calc" => new[] { "calc.exe" },
            "paint" or "mspaint" => new[] { "mspaint.exe" },
            "file explorer" or "explorer" or "files" => new[] { "explorer.exe" },
            "terminal" or "windows terminal" => new[] { "wt.exe", "powershell.exe" },
            "powershell" => new[] { "powershell.exe" },
            "command prompt" or "cmd" => new[] { "cmd.exe" },
            "task manager" => new[] { "taskmgr.exe" },
            "snipping tool" or "screenshot tool" => new[] { "snippingtool.exe" },
            "control panel" => new[] { "control.exe" },
            "registry editor" or "regedit" => new[] { "regedit.exe" },
            "vscode" or "visual studio code" or "code" => new[] { "code.exe", "code.cmd" },
            "chrome" or "google chrome" => new[] { "chrome.exe" },
            "edge" or "microsoft edge" => new[] { "msedge.exe" },
            "firefox" => new[] { "firefox.exe" },
            "spotify" => new[] { "spotify.exe" },
            "media player" or "windows media player" or "music" => new[] { "wmplayer.exe" },
            "discord" => new[] { "discord.exe" },
            "slack" => new[] { "slack.exe" },
            "teams" or "microsoft teams" => new[] { "ms-teams.exe", "teams.exe" },
            "outlook" => new[] { "outlook.exe" },
            _ => Array.Empty<string>(),
        };

        foreach (var candidate in candidates)
        {
            if (TryStart(candidate))
                return Logged("open_app", name, true, $"Opened {appName}.");
        }

        var shortcut = FindStartMenuShortcut(name);
        if (!string.IsNullOrWhiteSpace(shortcut) && TryStart(shortcut))
            return Logged("open_app", shortcut, true, $"Opened {Path.GetFileNameWithoutExtension(shortcut)}.");

        if (TryStartStartApp(appName))
            return Logged("open_app", name, true, $"Opened {appName}.");

        return Logged(
            "open_app",
            name,
            false,
            $"I could not find {appName}. Try its exact Start menu name or save it in a workflow.",
            supported: false);
    }

    private Dictionary<string, object> OpenFolder(string folder)
    {
        var name = NormalizeWords(folder);
        var path = name switch
        {
            "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            "documents" or "my documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "home" or "user folder" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "app data" or "keyboard data" => SettingsService.AppDataDir,
            "voice notes" => Models.KeyboardWtfState.EffectiveVoiceNoteSavePath,
            _ => folder?.Trim(),
        };
        return OpenPath(path);
    }

    private Dictionary<string, object> OpenPath(string path)
    {
        var expanded = ExpandPath(path);
        if (string.IsNullOrWhiteSpace(expanded) || (!File.Exists(expanded) && !Directory.Exists(expanded)))
            return Logged("open_path", path, false, "That file or folder was not found.", supported: false);
        if (File.Exists(expanded) && IsUnsafeExecutablePath(expanded))
            return Logged(
                "open_path",
                expanded,
                false,
                "Opening executable or script files by path is blocked. Ask me to open an installed app by name instead.",
                supported: false);

        Process.Start(new ProcessStartInfo { FileName = expanded, UseShellExecute = true });
        return Logged("open_path", expanded, true, $"Opened {Path.GetFileName(expanded.TrimEnd(Path.DirectorySeparatorChar))}.");
    }

    private Dictionary<string, object> WindowAction(string action, string appName)
    {
        var normalized = NormalizeWords(action);
        if (normalized == "show desktop")
        {
            keybd_event(VkLeftWindows, 0, 0, UIntPtr.Zero);
            keybd_event(VkD, 0, 0, UIntPtr.Zero);
            keybd_event(VkD, 0, KeyeventfKeyup, UIntPtr.Zero);
            keybd_event(VkLeftWindows, 0, KeyeventfKeyup, UIntPtr.Zero);
            return Logged("window_action", "show desktop", true, "Desktop shown.");
        }

        if (normalized is "list" or "list windows")
        {
            var windows = EnumerateWindows()
                .Take(15)
                .Select(w => new { w.Title, w.ProcessName })
                .ToArray();
            return Result(true, windows.Length == 0 ? "No normal app windows were found." : "Open windows listed.",
                extras: new() { ["windows"] = windows });
        }

        var handle = string.IsNullOrWhiteSpace(appName)
            ? GetForegroundWindow()
            : FindWindowByName(appName);
        if (handle == IntPtr.Zero)
            return Logged("window_action", $"{action} {appName}", false, "I could not find that window.", supported: false);

        switch (normalized)
        {
            case "switch":
            case "focus":
            case "activate":
                ShowWindow(handle, SwRestore);
                SetForegroundWindow(handle);
                return Logged("window_action", $"focus {appName}", true, "Switched to the window.");
            case "minimize":
                ShowWindow(handle, SwMinimize);
                return Logged("window_action", $"minimize {appName}", true, "Window minimized.");
            case "maximize":
                ShowWindow(handle, SwMaximize);
                return Logged("window_action", $"maximize {appName}", true, "Window maximized.");
            case "restore":
                ShowWindow(handle, SwRestore);
                return Logged("window_action", $"restore {appName}", true, "Window restored.");
            case "close":
            case "quit":
            case "exit":
                var title = GetWindowTitle(handle);
                var label = string.IsNullOrWhiteSpace(title)
                    ? string.IsNullOrWhiteSpace(appName) ? "the active window" : appName
                    : Limit(title, 80);
                PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
                return Logged("window_action", $"close {label}", true, $"Closed {label}.");
            default:
                return Result(false, "Supported window actions are list, switch, minimize, maximize, restore, and close.", supported: false);
        }
    }

    private Dictionary<string, object> BrowserAction(string action)
    {
        var activeProcess = GetProcessName(GetForegroundWindow());
        if (!IsBrowserProcess(activeProcess))
            return Result(
                false,
                "A supported browser must be the active window before I can control its tabs.",
                supported: false,
                needsClarification: true,
                extras: new() { ["active_process"] = activeProcess });

        var keys = NormalizeWords(action) switch
        {
            "new tab" => "^t",
            "close tab" => "^w",
            "next tab" => "^{TAB}",
            "previous tab" => "^+{TAB}",
            "reopen tab" => "^+t",
            "refresh" or "reload" => "^r",
            "back" => "%{LEFT}",
            "forward" => "%{RIGHT}",
            "focus address" or "address bar" => "^l",
            "find" => "^f",
            "downloads" => "^j",
            "history" => "^h",
            _ => null,
        };
        if (keys == null)
            return Result(false, "That browser action is not supported yet.", supported: false);
        SendKeysOnUiThread(keys);
        return Logged("browser_action", action, true, $"Browser action completed: {action}.");
    }

    private Dictionary<string, object> WebSearch(string query, string engine)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Result(false, "What should I search for?", needsClarification: true);
        var encoded = Uri.EscapeDataString(query.Trim());
        var url = NormalizeWords(engine) switch
        {
            "youtube" => $"https://www.youtube.com/results?search_query={encoded}",
            "github" => $"https://github.com/search?q={encoded}",
            _ => $"https://www.google.com/search?q={encoded}",
        };
        return OpenUrl(url);
    }

    private Dictionary<string, object> GetDesktopContext()
    {
        var handle = GetForegroundWindow();
        var title = GetWindowTitle(handle);
        var processName = GetProcessName(handle);
        var clipboard = Limit(ClipboardHelper.GetText(), 2500);
        return Result(true, "Desktop context captured.", extras: new()
        {
            ["active_window_title"] = title,
            ["active_process"] = processName,
            ["clipboard_text"] = clipboard,
            ["clipboard_truncated"] = clipboard.Length >= 2500,
        });
    }

    private Dictionary<string, object> GetClipboardText()
    {
        var text = Limit(ClipboardHelper.GetText(), 6000);
        return Result(true, string.IsNullOrWhiteSpace(text) ? "Clipboard is empty." : "Clipboard text captured.",
            extras: new() { ["text"] = text });
    }

    private async Task<Dictionary<string, object>> GetSelectedTextAsync(CancellationToken token)
    {
        var previous = ClipboardHelper.Capture();
        var marker = $"keyboard-wtf-{Guid.NewGuid():N}";
        string selected;
        try
        {
            ClipboardHelper.SetText(marker);
            SendKeysOnUiThread("^c");
            await Task.Delay(180, token);
            selected = ClipboardHelper.GetText();
        }
        finally
        {
            ClipboardHelper.Restore(previous);
        }
        if (selected == marker || string.IsNullOrWhiteSpace(selected))
            return Result(false, "No selected text was detected. Select text first and try again.", supported: false);
        return Result(true, "Selected text captured.", extras: new()
        {
            ["text"] = Limit(selected, 6000),
            ["truncated"] = selected.Length > 6000,
        });
    }

    private async Task<Dictionary<string, object>> ReplaceSelectedTextAsync(string text, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Result(false, "What text should replace the selection?", needsClarification: true);
        var previous = ClipboardHelper.Capture();
        try
        {
            ClipboardHelper.SetText(text);
            SendKeysOnUiThread("^v");
            await Task.Delay(220, token);
        }
        finally
        {
            ClipboardHelper.Restore(previous);
        }
        return Logged("replace_selected_text", Limit(text, 120), true, "Replaced the selected text.");
    }

    private async Task<Dictionary<string, object>> TypeTextAsync(string text, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Result(false, "What should I type?", needsClarification: true);
        var previous = ClipboardHelper.Capture();
        try
        {
            ClipboardHelper.SetText(text);
            SendKeysOnUiThread("^v");
            await Task.Delay(220, token);
        }
        finally
        {
            ClipboardHelper.Restore(previous);
        }
        return Logged("type_text", Limit(text, 120), true, "Typed the text into the active field.");
    }

    private Dictionary<string, object> PressKey(string key)
    {
        var sequence = NormalizeWords(key) switch
        {
            "tab" => "{TAB}",
            "escape" or "esc" => "{ESC}",
            "up" => "{UP}",
            "down" => "{DOWN}",
            "left" => "{LEFT}",
            "right" => "{RIGHT}",
            "page up" => "{PGUP}",
            "page down" => "{PGDN}",
            "home" => "{HOME}",
            "end" => "{END}",
            _ => null,
        };
        if (sequence == null)
            return Result(false, "That key is not supported.", supported: false);
        SendKeysOnUiThread(sequence);
        return Logged("press_key", key, true, $"Pressed {key}.");
    }

    private async Task<Dictionary<string, object>> SearchFilesAsync(
        string query,
        string location,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Result(false, "What file or folder should I search for?", needsClarification: true);

        var roots = ResolveSearchRoots(location);
        var matches = await Task.Run(() =>
        {
            var found = new List<string>();
            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                    continue;
                SearchDirectory(root, query.Trim(), found, 25, token);
                if (found.Count >= 25)
                    break;
            }
            return found;
        }, token);

        _history.Add("search_files", $"{query} in {location}", true);
        return Result(true, matches.Count == 0 ? "No matching files or folders were found." : $"Found {matches.Count} matches.",
            extras: new() { ["matches"] = matches });
    }

    private Dictionary<string, object> SaveNote(string text, string title)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Result(false, "What should I save in the note?", needsClarification: true);
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "keyboard.wtf");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "Jarvis Notes.md");
        var heading = string.IsNullOrWhiteSpace(title) ? "Quick note" : title.Trim();
        File.AppendAllText(path, $"{Environment.NewLine}## {heading} - {DateTime.Now:g}{Environment.NewLine}{text.Trim()}{Environment.NewLine}");
        return Logged("save_note", path, true, "Saved the note.", extras: new() { ["path"] = path });
    }

    private Dictionary<string, object> AddTodo(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Result(false, "What should I add to the to-do list?", needsClarification: true);
        var todos = LoadTodos();
        todos.Add(new JarvisTodo { Text = Limit(text, 240) });
        SaveTodos(todos);
        return Logged("add_todo", text, true, "Added to the to-do list.");
    }

    private Dictionary<string, object> ListTodos()
    {
        var todos = LoadTodos();
        return Result(true, todos.Count == 0 ? "The to-do list is empty." : "To-do list loaded.", extras: new()
        {
            ["todos"] = todos.Select(t => new { t.Id, t.Text, t.Completed, t.CreatedAt }).ToArray(),
        });
    }

    private Dictionary<string, object> CompleteTodo(string query)
    {
        var todos = LoadTodos();
        var todo = todos.FirstOrDefault(t =>
            !t.Completed && t.Text.Contains(query ?? "", StringComparison.OrdinalIgnoreCase));
        if (todo == null)
            return Result(false, "I could not find a matching unfinished to-do.", supported: false);
        todo.Completed = true;
        SaveTodos(todos);
        return Logged("complete_todo", todo.Text, true, "Marked the to-do complete.");
    }

    private Dictionary<string, object> SetTimer(int minutes, string label)
    {
        minutes = Math.Clamp(minutes, 1, 1440);
        var timerLabel = string.IsNullOrWhiteSpace(label) ? $"{minutes} minute timer" : Limit(label, 80);
        var id = Guid.NewGuid().ToString("N");
        var timer = new System.Threading.Timer(_ =>
        {
            _notifications.Info("Jarvis timer", timerLabel);
            _history.Add("timer_finished", timerLabel, true);
            lock (_timers)
            {
                if (_timers.Remove(id, out var completed))
                    completed.Dispose();
            }
        }, null, TimeSpan.FromMinutes(minutes), Timeout.InfiniteTimeSpan);
        lock (_timers)
            _timers[id] = timer;
        return Logged("set_timer", $"{minutes} minutes: {timerLabel}", true, $"Timer set for {minutes} minutes.");
    }

    private Dictionary<string, object> SystemControl(string action, int value)
    {
        var normalized = NormalizeWords(action);
        switch (normalized)
        {
            case "volume up":
                PressMediaKey(VkVolumeUp, Math.Clamp(value <= 0 ? 2 : value, 1, 20));
                return Logged("system_control", action, true, "Volume increased.");
            case "volume down":
                PressMediaKey(VkVolumeDown, Math.Clamp(value <= 0 ? 2 : value, 1, 20));
                return Logged("system_control", action, true, "Volume decreased.");
            case "mute":
            case "toggle mute":
                PressMediaKey(VkVolumeMute, 1);
                return Logged("system_control", action, true, "Mute toggled.");
            case "bluetooth settings":
                return OpenUrl("ms-settings:bluetooth");
            case "wifi settings":
                return OpenUrl("ms-settings:network-wifi");
            case "display settings":
                return OpenUrl("ms-settings:display");
            case "sound settings":
                return OpenUrl("ms-settings:sound");
            default:
                return Result(false, "Supported direct controls are volume, mute, and opening Wi-Fi, Bluetooth, display, or sound settings.", supported: false);
        }
    }

    private Dictionary<string, object> SystemStatus()
    {
        var power = SystemInformation.PowerStatus;
        var batteryPercent = power.BatteryLifePercent < 0
            ? (int?)null
            : (int)Math.Round(power.BatteryLifePercent * 100);
        return Result(true, "System status captured.", extras: new()
        {
            ["battery_percent"] = batteryPercent,
            ["plugged_in"] = power.PowerLineStatus == PowerLineStatus.Online,
            ["uptime_minutes"] = (long)TimeSpan.FromMilliseconds(Environment.TickCount64).TotalMinutes,
            ["machine_name"] = Environment.MachineName,
            ["user_name"] = Environment.UserName,
            ["os"] = Environment.OSVersion.VersionString,
        });
    }

    private Dictionary<string, object> PlayMedia(string query, string service)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Result(false, "What should I play?", needsClarification: true);

        var cleanQuery = query.Trim();
        var normalizedService = NormalizeWords(service);
        if (string.IsNullOrWhiteSpace(normalizedService))
            normalizedService = "spotify";

        if (normalizedService is "spotify" or "spotify app")
        {
            var encoded = Uri.EscapeDataString(cleanQuery);
            if (TryStart($"spotify:search:{encoded}"))
            {
                return Logged(
                    "play_media",
                    $"Spotify: {cleanQuery}",
                    true,
                    $"Opened Spotify search for {cleanQuery}. Select the result and press Play.",
                    extras: new()
                    {
                        ["service"] = "spotify",
                        ["direct_playback"] = false,
                        ["requires_spotify_oauth"] = true,
                    });
            }

            var result = OpenUrl($"https://open.spotify.com/search/{encoded}");
            result["direct_playback"] = false;
            result["requires_spotify_oauth"] = true;
            result["message"] = $"Opened Spotify search for {cleanQuery}. Select the result and press Play.";
            return result;
        }

        if (normalizedService is "youtube" or "youtube music")
            return OpenUrl($"https://www.youtube.com/results?search_query={Uri.EscapeDataString(cleanQuery)}");

        return Result(false, "Supported media services are Spotify and YouTube.", supported: false);
    }

    private Dictionary<string, object> OpenServicePage(string service, string page, string query)
    {
        var normalizedService = NormalizeWords(service);
        var normalizedPage = NormalizeWords(page);
        var encoded = Uri.EscapeDataString(query?.Trim() ?? "");

        if (normalizedService is "amazon" or "amazon india" or "amazon.in")
        {
            if (normalizedPage is not ("search" or "products"))
                return Result(
                    false,
                    "Amazon cart changes are not supported safely yet. I can open a product search for manual review.",
                    supported: false);
            if (string.IsNullOrWhiteSpace(query))
                return Result(false, "What should I search for on Amazon?", needsClarification: true);
            return OpenUrl($"https://www.amazon.in/s?k={encoded}");
        }

        if (normalizedService is "youtube")
        {
            if (normalizedPage is "liked videos" or "liked playlist" or "likes")
                return OpenUrl("https://www.youtube.com/playlist?list=LL");
            if (normalizedPage is "subscriptions")
                return OpenUrl("https://www.youtube.com/feed/subscriptions");
            if (normalizedPage is "history")
                return OpenUrl("https://www.youtube.com/feed/history");
            return Result(
                false,
                "Bulk unlike and account-changing YouTube actions are not supported safely. I can open Liked Videos, Subscriptions, or History.",
                supported: false);
        }

        if (normalizedService is "spotify")
        {
            if (normalizedPage is "liked songs" or "liked music" or "library")
            {
                if (TryStart("spotify:collection:tracks"))
                    return Logged("open_service_page", "Spotify liked songs", true, "Opened Spotify Liked Songs.");
                return OpenUrl("https://open.spotify.com/collection/tracks");
            }
            return Result(false, "Supported Spotify pages are Liked Songs and media search.", supported: false);
        }

        if (normalizedService is "discord")
        {
            if (normalizedPage is "home" or "app" or "friends")
            {
                if (TryStart("discord://"))
                    return Logged("open_service_page", "Discord", true, "Opened Discord.");
                return OpenUrl("https://discord.com/app");
            }
            return Result(
                false,
                "Discord messaging is not configured in this build. It requires a registered Discord app, OAuth permission, and a stable recipient ID; personal-account UI automation is intentionally blocked.",
                supported: false);
        }

        return Result(false, "That service page is not supported yet.", supported: false);
    }

    private Dictionary<string, object> OpenCamera()
    {
        if (!TryStart("microsoft.windows.camera:"))
            return Logged("open_camera", "", false, "Windows Camera could not be opened.", supported: false);

        return Logged(
            "open_camera",
            "Windows Camera",
            true,
            "Opened Windows Camera. Automatic shutter control is not enabled; use the Camera button to take the photo.",
            extras: new() { ["automatic_capture"] = false });
    }

    private Dictionary<string, object> TakeScreenshot()
    {
        var bounds = SystemInformation.VirtualScreen;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return Logged("take_screenshot", "", false, "The screen could not be captured.", supported: false);

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "keyboard.wtf",
            "Screenshots");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png");

        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        bitmap.Save(path, ImageFormat.Png);
        return Logged(
            "take_screenshot",
            path,
            true,
            "Screenshot saved.",
            extras: new() { ["path"] = path });
    }

    private Dictionary<string, object> OpenGmailDraft(string recipient, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(recipient))
            return Result(false, "Who should I address the email to?", needsClarification: true);
        if (string.IsNullOrWhiteSpace(body))
            return Result(false, "What should the email say?", needsClarification: true);

        var query = new List<string> { "view=cm", "fs=1" };
        query.Add($"to={Uri.EscapeDataString(recipient.Trim())}");
        if (!string.IsNullOrWhiteSpace(subject))
            query.Add($"su={Uri.EscapeDataString(subject.Trim())}");
        query.Add($"body={Uri.EscapeDataString(body.Trim())}");
        ClipboardHelper.SetText(body.Trim());

        var result = OpenUrl("https://mail.google.com/mail/?" + string.Join("&", query));
        result["message"] = "Gmail draft opened. Review it before sending.";
        result["manual_send_required"] = true;
        return result;
    }

    private Dictionary<string, object> PrepareWhatsAppMessage(string message, string contactName, string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(message))
            return Result(false, "What should the WhatsApp message say?", needsClarification: true);

        ClipboardHelper.SetText(message.Trim());
        if (!string.IsNullOrWhiteSpace(phoneNumber))
        {
            var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
            if (digits.Length < 7)
                return Result(false, "That phone number does not look usable.", needsClarification: true);

            var encoded = Uri.EscapeDataString(message.Trim());
            if (!TryStart($"whatsapp://send?phone={digits}&text={encoded}"))
                OpenUrl($"https://wa.me/{digits}?text={encoded}");
            return Logged(
                "prepare_whatsapp_message",
                digits,
                true,
                "WhatsApp message prepared. Review it before sending.",
                extras: new() { ["manual_send_required"] = true });
        }

        if (!TryStart("whatsapp://"))
            OpenUrl("https://web.whatsapp.com/");
        var contact = string.IsNullOrWhiteSpace(contactName) ? "that contact" : contactName.Trim();
        return Logged(
            "prepare_whatsapp_message",
            contact,
            false,
            $"I cannot reliably select {contact} by name yet. I copied the message and opened WhatsApp.",
            supported: false,
            extras: new() { ["contact_lookup_supported"] = false });
    }

    private Dictionary<string, object> CopyText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Result(false, "What should I copy?", needsClarification: true);
        ClipboardHelper.SetText(text);
        return Logged("copy_text", Limit(text, 120), true, "Copied to clipboard.");
    }

    private Dictionary<string, object> CreateWorkflow(JsonElement args)
    {
        var workflow = _settings.SaveWorkflow(
            GetString(args, "name"),
            GetString(args, "apps"),
            GetString(args, "urls"),
            GetString(args, "folder"));
        return Logged("create_workflow", workflow.Name, true, $"Saved workflow {workflow.Name}.");
    }

    private Dictionary<string, object> ListWorkflows() =>
        Result(true, "Workflows loaded.", extras: new()
        {
            ["workflows"] = (_settings.Current.JarvisWorkflows ?? new()).Select(w => new
            {
                w.Name,
                w.Apps,
                w.Urls,
                w.Folder,
            }).ToArray(),
            ["built_in_examples"] = new[] { "coding mode", "study mode", "hackathon mode" },
        });

    private async Task<Dictionary<string, object>> RunWorkflowAsync(string name, CancellationToken token)
    {
        var workflow = FindWorkflow(name);
        if (workflow == null)
            return Result(false, $"Workflow {name} was not found. Create it first or use a built-in example.", supported: false);

        var failures = new List<string>();
        foreach (var app in SplitList(workflow.Apps))
        {
            token.ThrowIfCancellationRequested();
            var result = OpenApp(app);
            if (!Succeeded(result))
                failures.Add(result["message"]?.ToString() ?? $"Could not open {app}.");
            await Task.Delay(180, token);
        }
        foreach (var url in SplitList(workflow.Urls))
        {
            token.ThrowIfCancellationRequested();
            var result = OpenUrl(NormalizeUrl(url));
            if (!Succeeded(result))
                failures.Add(result["message"]?.ToString() ?? $"Could not open {url}.");
            await Task.Delay(180, token);
        }
        if (!string.IsNullOrWhiteSpace(workflow.Folder))
        {
            var result = OpenFolder(workflow.Folder);
            if (!Succeeded(result))
                failures.Add(result["message"]?.ToString() ?? $"Could not open {workflow.Folder}.");
        }

        return Logged(
            "run_workflow",
            workflow.Name,
            failures.Count == 0,
            failures.Count == 0
                ? $"Started {workflow.Name}."
                : $"Started {workflow.Name} with {failures.Count} action failure(s).",
            extras: new() { ["failures"] = failures.ToArray() });
    }

    private Dictionary<string, object> DeleteWorkflow(string name)
    {
        var removed = _settings.DeleteWorkflow(name);
        return Logged("delete_workflow", name, removed, removed ? "Workflow deleted." : "Workflow not found.");
    }

    private Dictionary<string, object> RequestSensitiveAction(string action, string target)
    {
        var normalized = NormalizeWords(action);
        var allowed = new[]
        {
            "close active app", "close app", "shutdown", "restart", "sleep", "lock screen", "disable wifi",
        };
        if (!allowed.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            return Result(false, "That sensitive action is not supported.", supported: false);
        if (normalized == "close app" && string.IsNullOrWhiteSpace(target))
            return Result(false, "Which app should I close?", needsClarification: true);

        if (_settings.Current.JarvisPermissionMode == JarvisPermissionMode.AutoExecute
            && normalized is "close active app" or "close app")
        {
            var result = WindowAction("close", normalized == "close app" ? target : "");
            result["auto_executed"] = true;
            return result;
        }

        var description = normalized switch
        {
            "close active app" => "Close the currently active app",
            "close app" => $"Close {target}",
            "shutdown" => "Shut down this computer",
            "restart" => "Restart this computer",
            "sleep" => "Put this computer to sleep",
            "lock screen" => "Lock this computer",
            "disable wifi" => "Disable Wi-Fi and disconnect Jarvis",
            _ => normalized,
        };

        lock (_pendingLock)
        {
            _pending = new PendingJarvisAction
            {
                IsSensitive = true,
                Action = normalized,
                Target = target ?? "",
                Description = description,
            };
        }
        Models.KeyboardWtfState.SetUi(Models.VoiceUiPhase.Thinking, "Confirmation required", description);
        _history.Add("confirmation_requested", description, true, true);
        return Result(true, $"{description}. Ask the user to say confirm or cancel.", confirmationRequired: true,
            extras: new() { ["description"] = description });
    }

    private async Task<Dictionary<string, object>> ConfirmPendingActionAsync(CancellationToken token)
    {
        PendingJarvisAction pending;
        lock (_pendingLock)
            pending = _pending;
        if (pending == null)
            return Result(false, "There is no pending action.", supported: false);
        if (pending.ConfirmedAt == null
            || pending.ConfirmedAt < pending.RequestedAt
            || DateTimeOffset.UtcNow - pending.ConfirmedAt > TimeSpan.FromSeconds(30))
        {
            return Result(false, "The user has not confirmed this action yet. Ask them to say confirm or cancel.",
                confirmationRequired: true);
        }

        lock (_pendingLock)
            _pending = null;
        if (pending.IsSensitive)
            return ExecuteSensitiveAction(pending);

        _executingApprovedAction = true;
        try
        {
            return await ExecuteCoreAsync(pending.ToolName, pending.Args, token);
        }
        finally
        {
            _executingApprovedAction = false;
        }
    }

    private Dictionary<string, object> CancelPendingAction()
    {
        lock (_pendingLock)
            _pending = null;
        return Result(true, "Pending action cancelled.");
    }

    private Dictionary<string, object> ExecuteSensitiveAction(PendingJarvisAction pending)
    {
        switch (pending.Action)
        {
            case "close active app":
            {
                var handle = GetForegroundWindow();
                if (handle == IntPtr.Zero)
                    return Logged("close_app", "active app", false, "No active app window was found.");
                PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
                return Logged("close_app", GetWindowTitle(handle), true, "Closed the active app.");
            }
            case "close app":
            {
                var handle = FindWindowByName(pending.Target);
                if (handle == IntPtr.Zero)
                    return Logged("close_app", pending.Target, false, "That app window was not found.");
                PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
                return Logged("close_app", pending.Target, true, $"Closed {pending.Target}.");
            }
            case "lock screen":
                LockWorkStation();
                return Logged("lock_screen", "", true, "Computer locked.");
            case "shutdown":
                Process.Start(new ProcessStartInfo { FileName = "shutdown.exe", Arguments = "/s /t 0", UseShellExecute = false });
                return Logged("shutdown", "", true, "Shutting down.");
            case "restart":
                Process.Start(new ProcessStartInfo { FileName = "shutdown.exe", Arguments = "/r /t 0", UseShellExecute = false });
                return Logged("restart", "", true, "Restarting.");
            case "sleep":
                Process.Start(new ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = "powrprof.dll,SetSuspendState 0,1,0",
                    UseShellExecute = false,
                });
                return Logged("sleep", "", true, "Going to sleep.");
            case "disable wifi":
                Process.Start(new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = "interface set interface name=\"Wi-Fi\" admin=disabled",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                return Logged("disable_wifi", "", true, "Wi-Fi disable requested.");
            default:
                return Result(false, "Sensitive action is no longer supported.", supported: false);
        }
    }

    private Dictionary<string, object> OpenUrl(string url)
    {
        if (url?.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) == true)
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            return Logged("open_settings_page", url, true, "Opened Windows settings.");
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return Logged("open_url", url, false, "Only http and https links can be opened.", supported: false);
        Process.Start(new ProcessStartInfo { FileName = uri.ToString(), UseShellExecute = true });
        return Logged("open_url", uri.ToString(), true, $"Opened {uri.Host}.");
    }

    private JarvisWorkflowSettings FindWorkflow(string name)
    {
        var normalized = NormalizeWords(name);
        var saved = _settings.Current.JarvisWorkflows?.FirstOrDefault(w =>
            string.Equals(NormalizeWords(w.Name), normalized, StringComparison.OrdinalIgnoreCase));
        if (saved != null)
            return saved;
        return normalized switch
        {
            "coding mode" => new JarvisWorkflowSettings
            {
                Name = "Coding mode",
                Apps = "Visual Studio Code, Windows Terminal",
                Urls = "https://github.com/",
            },
            "study mode" => new JarvisWorkflowSettings
            {
                Name = "Study mode",
                Apps = "Notepad",
                Urls = "https://www.youtube.com/results?search_query=focus+music",
            },
            "hackathon mode" => new JarvisWorkflowSettings
            {
                Name = "Hackathon mode",
                Apps = "Visual Studio Code, Windows Terminal",
                Urls = "https://devpost.com/, https://github.com/",
            },
            _ => null,
        };
    }

    private Dictionary<string, object> Logged(
        string action,
        string detail,
        bool success,
        string message,
        bool supported = true,
        Dictionary<string, object> extras = null)
    {
        _history.Add(action, detail, success);
        return Result(success, message, supported: supported, extras: extras);
    }

    private static Dictionary<string, object> Result(
        bool ok,
        string message,
        bool supported = true,
        bool needsClarification = false,
        bool confirmationRequired = false,
        Dictionary<string, object> extras = null)
    {
        var result = extras ?? new Dictionary<string, object>();
        result["ok"] = ok;
        result["supported"] = supported;
        result["needs_clarification"] = needsClarification;
        result["confirmation_required"] = confirmationRequired;
        result["message"] = message ?? "";
        return result;
    }

    private static string GetString(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : value.ToString();
    }

    private static int GetInt(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value))
            return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        return int.TryParse(value.ToString(), out number) ? number : 0;
    }

    private static string NormalizeWords(string text) =>
        string.Join(" ", (text ?? "").Trim().ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string NormalizePhraseText(string text)
    {
        var wordsOnly = new string((text ?? "").ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character == '\''
                ? character
                : ' ')
            .ToArray());
        return NormalizeWords(wordsOnly);
    }

    private static bool ContainsPhrase(string value, params string[] phrases)
    {
        var padded = $" {value} ";
        return phrases.Any(phrase =>
            padded.Contains($" {NormalizePhraseText(phrase)} ", StringComparison.OrdinalIgnoreCase));
    }

    private static bool Succeeded(Dictionary<string, object> result) =>
        result.TryGetValue("ok", out var ok) && ok is bool success && success;

    private static bool IsUnsafeExecutablePath(string path)
    {
        var extension = Path.GetExtension(path);
        return new[]
        {
            ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".msi", ".msix", ".appx",
            ".scr", ".reg", ".lnk", ".url", ".hta", ".js", ".jse", ".vbs", ".vbe", ".wsf",
        }.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryStart(string fileName)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = fileName, UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    private static bool TryStartPackagedApp(params string[] appIds)
    {
        foreach (var appId in appIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                info.ArgumentList.Add($@"shell:AppsFolder\{appId}");
                Process.Start(info);
                return true;
            }
            catch { }
        }

        return false;
    }

    private static bool TryStartStartApp(params string[] names)
    {
        foreach (var name in names.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var appId = FindStartAppId(name);
            if (!string.IsNullOrWhiteSpace(appId) && TryStartPackagedApp(appId))
                return true;
        }

        return false;
    }

    private static string FindStartMenuShortcut(string name)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        };
        foreach (var root in roots)
        {
            try
            {
                var match = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories)
                    .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path)
                        .Contains(name, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }
            catch { }
        }
        return null;
    }

    private static string FindStartAppId(string name)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-ExecutionPolicy");
            info.ArgumentList.Add("Bypass");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add(BuildStartAppLookupCommand(name));

            using var process = Process.Start(info);
            if (process == null)
                return null;
            if (!process.WaitForExit(3500))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            return process.StandardOutput.ReadToEnd().Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string BuildStartAppLookupCommand(string name)
    {
        var safe = (name ?? "").Replace("'", "''");
        return
            "$needle = '" + safe + "'; " +
            "$exact = Get-StartApps | Where-Object { $_.Name -eq $needle } | Select-Object -First 1 -ExpandProperty AppID; " +
            "if ($exact) { $exact; exit } " +
            "$contains = Get-StartApps | Where-Object { $_.Name -like ('*' + $needle + '*') -or $needle -like ('*' + $_.Name + '*') } | Select-Object -First 1 -ExpandProperty AppID; " +
            "if ($contains) { $contains }";

    }

    private static string ExpandPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        return Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
    }

    private static List<WindowInfo> EnumerateWindows()
    {
        var windows = new List<WindowInfo>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
                return true;
            var title = GetWindowTitle(handle);
            if (string.IsNullOrWhiteSpace(title))
                return true;
            windows.Add(new WindowInfo(handle, title, GetProcessName(handle)));
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static IntPtr FindWindowByName(string name)
    {
        var normalized = NormalizeWords(name);
        return EnumerateWindows()
            .FirstOrDefault(w => w.Title.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                || w.ProcessName.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            ?.Handle ?? IntPtr.Zero;
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
            return "";
        var builder = new StringBuilder(length + 1);
        GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetProcessName(IntPtr handle)
    {
        GetWindowThreadProcessId(handle, out var processId);
        try { return Process.GetProcessById((int)processId).ProcessName; }
        catch { return ""; }
    }

    private static bool IsBrowserProcess(string processName) =>
        processName is not null
        && new[] { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc" }
            .Contains(processName, StringComparer.OrdinalIgnoreCase);

    private static void SendKeysOnUiThread(string keys)
    {
        var form = Application.OpenForms.Cast<Form>().FirstOrDefault();
        if (form?.InvokeRequired == true)
            form.Invoke(() => SendKeys.SendWait(keys));
        else
            SendKeys.SendWait(keys);
    }

    private static void PressMediaKey(byte key, int count)
    {
        for (var i = 0; i < count; i++)
        {
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, KeyeventfKeyup, UIntPtr.Zero);
        }
    }

    private static IReadOnlyList<string> ResolveSearchRoots(string location)
    {
        var normalized = NormalizeWords(location);
        if (normalized == "desktop")
            return new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) };
        if (normalized == "downloads")
            return new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") };
        if (normalized == "documents")
            return new[] { Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
        var expanded = ExpandPath(location);
        if (Directory.Exists(expanded))
            return new[] { expanded };
        return new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
    }

    private static void SearchDirectory(
        string root,
        string query,
        List<string> found,
        int limit,
        CancellationToken token)
    {
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0 && found.Count < limit)
        {
            token.ThrowIfCancellationRequested();
            var current = stack.Pop();
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(current.Path))
                {
                    token.ThrowIfCancellationRequested();
                    if (Path.GetFileName(entry).Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(entry);
                        if (found.Count >= limit)
                            break;
                    }
                    if (current.Depth < 5 && Directory.Exists(entry)
                        && !File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
                        stack.Push((entry, current.Depth + 1));
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }

    private List<JarvisTodo> LoadTodos()
    {
        try
        {
            if (!File.Exists(_todosPath))
                return new List<JarvisTodo>();
            return JsonSerializer.Deserialize<List<JarvisTodo>>(File.ReadAllText(_todosPath)) ?? new();
        }
        catch { return new List<JarvisTodo>(); }
    }

    private void SaveTodos(List<JarvisTodo> todos)
    {
        Directory.CreateDirectory(SettingsService.AppDataDir);
        File.WriteAllText(_todosPath, JsonSerializer.Serialize(todos, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static IEnumerable<string> SplitList(string value) =>
        (value ?? "").Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string NormalizeUrl(string value)
    {
        var url = value?.Trim() ?? "";
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "https://" + url;
        return url;
    }

    private static string Limit(string value, int maxLength)
    {
        var text = value ?? "";
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        lock (_timers)
        {
            foreach (var timer in _timers.Values)
                timer.Dispose();
            _timers.Clear();
        }
    }

    private sealed class PendingJarvisAction
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string ToolName { get; init; } = "";
        public JsonElement Args { get; init; }
        public bool IsSensitive { get; init; }
        public string Action { get; init; } = "";
        public string Target { get; init; } = "";
        public string Description { get; init; } = "";
        public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? ConfirmedAt { get; set; }
    }

    private sealed class JarvisTodo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Text { get; set; } = "";
        public bool Completed { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    }

    private sealed record WindowInfo(IntPtr Handle, string Title, string ProcessName);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool LockWorkStation();
    [DllImport("user32.dll")] private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
