namespace KeyboardWtf.Services;

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KeyboardWtf.Models;
using NAudio.Wave;

public sealed class WebSettingsService : IDisposable
{
    private static WebSettingsService _instance;
    private static readonly object _lock = new();

    private HttpListener _listener;
    private CancellationTokenSource _cts;
    private Task _serverTask;
    private bool _disposed;

    private KeyboardWtfApp _app;
    private SettingsService _settings;
    private HotkeyService _hotkeys;
    private IntentMemoryService _intentMemory;
    private JarvisActionHistoryService _jarvisHistory;
    private string _settingsHtml;

    // Live microphone meter state
    private WaveInEvent _liveMic;
    private volatile int _liveMicPeak;
    private readonly object _liveMicLock = new();

    public int Port { get; private set; }
    public bool IsRunning { get; private set; }

    private WebSettingsService() { }

    public static WebSettingsService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new WebSettingsService();
                }
            }
            return _instance;
        }
    }

    public void Initialize(
        KeyboardWtfApp app,
        SettingsService settings,
        HotkeyService hotkeys,
        IntentMemoryService intentMemory,
        JarvisActionHistoryService jarvisHistory)
    {
        _app = app;
        _settings = settings;
        _hotkeys = hotkeys;
        _intentMemory = intentMemory;
        _jarvisHistory = jarvisHistory;
        _settingsHtml = LoadSettingsHtml();
    }

    public void Start()
    {
        if (IsRunning) return;

        Port = ResolveBridgePort();
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{Port}/");

        try
        {
            _listener.Start();
            IsRunning = true;
            _serverTask = Task.Run(() => ListenLoop(_cts.Token));
            AppLog.Info($"WebSettingsService started on port {Port}");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to start web server: {ex.Message}");
            _listener = null;
        }
    }

    public void OpenInBrowser(string fragment = "")
    {
        if (!IsRunning) Start();
        if (!IsRunning) return;

        var suffix = string.IsNullOrWhiteSpace(fragment)
            ? ""
            : fragment.StartsWith("#", StringComparison.Ordinal) ? fragment : "#" + fragment;
        var url = $"http://localhost:{Port}/{suffix}";
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to open browser: {ex.Message}");
        }
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequest(context), ct);
            }
            catch (ObjectDisposedException) { break; }
            catch (HttpListenerException) { break; }
            catch (Exception ex)
            {
                AppLog.Error($"Web server error: {ex.Message}");
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        var req = context.Request;
        var resp = context.Response;

        try
        {
            // Security: only serve localhost
            if (!req.IsLocal)
            {
                resp.StatusCode = 403;
                resp.Close();
                return;
            }

            var path = req.Url.AbsolutePath;
            var method = req.HttpMethod;

            if (path.StartsWith("/api/bridge/", StringComparison.OrdinalIgnoreCase))
            {
                if (!ApplyBridgeCors(req, resp))
                {
                    resp.StatusCode = 403;
                    resp.Close();
                    return;
                }

                if (method == "OPTIONS")
                {
                    resp.StatusCode = 204;
                    resp.Close();
                    return;
                }
            }

            if (path == "/" && method == "GET")
            {
                await ServeHtml(resp);
            }
            else if (path == "/api/settings" && method == "GET")
            {
                await ServeSettings(resp);
            }
            else if (path == "/api/settings" && method == "POST")
            {
                await SaveSettings(req, resp);
            }
            else if (path == "/api/hotkeys/reset" && method == "POST")
            {
                _settings?.ResetHotkeys();
                _app?.RegisterHotkeys();
                await WriteJson(resp, new { ok = true });
            }
            else if (path == "/api/intent-memory/delete" && method == "POST")
            {
                await DeleteIntentMemory(req, resp);
            }
            else if (path == "/api/intent-memory/remember" && method == "POST")
            {
                await RememberIntentMemory(req, resp);
            }
            else if (path == "/api/intent-memory/clear" && method == "POST")
            {
                _intentMemory?.Clear();
                await WriteJson(resp, new { ok = true });
            }
            else if (path == "/api/jarvis-history/clear" && method == "POST")
            {
                _jarvisHistory?.Clear();
                await WriteJson(resp, new { ok = true });
            }
            else if (path == "/api/jarvis-workflow/delete" && method == "POST")
            {
                await DeleteJarvisWorkflow(req, resp);
            }
            else if (path == "/api/jarvis/stop" && method == "POST")
            {
                _app?.Commands?.CancelCurrent();
                await WriteJson(resp, new { ok = true });
            }
            else if (path == "/api/microphones" && method == "GET")
            {
                await ServeMicrophones(resp);
            }
            else if (path == "/api/mic-test" && method == "POST")
            {
                await RunMicTest(resp);
            }
            else if (path == "/api/mic-test-start" && method == "POST")
            {
                await StartLiveMicTest(resp);
            }
            else if (path == "/api/mic-level" && method == "GET")
            {
                await GetLiveMicLevel(resp);
            }
            else if (path == "/api/mic-test-stop" && method == "POST")
            {
                await StopLiveMicTest(resp);
            }
            else if (path == "/api/download-models" && method == "POST")
            {
                await DownloadModels(resp);
            }
            else if (path == "/api/tts/install" && method == "POST")
            {
                await InstallPiper(resp);
            }
            else if (path == "/api/tts/test" && method == "POST")
            {
                await TestTts(req, resp);
            }
            else if (path == "/api/test-ai" && method == "POST")
            {
                await TestAiProvider(req, resp);
            }
            else if (path == "/api/bridge/status" && method == "GET")
            {
                await ServeBridgeStatus(req, resp);
            }
            else if (path == "/api/bridge/action" && method == "POST")
            {
                await ExecuteBridgeAction(req, resp);
            }
            else if (path == "/api/bridge/regenerate-token" && method == "POST")
            {
                _settings?.RegenerateBridgePairingToken();
                await WriteJson(resp, new { ok = true, pairingToken = KeyboardWtfState.BridgePairingToken });
            }
            else if (path == "/api/open-voice-notes" && method == "POST")
            {
                await OpenVoiceNotesFolder(resp);
            }
            else if (path == "/api/pick-voice-notes-folder" && method == "POST")
            {
                await PickVoiceNotesFolder(resp);
            }
            else if (path == "/api/preview-filename" && method == "POST")
            {
                await PreviewFilenameEndpoint(req, resp);
            }
            else if (path == "/api/exit" && method == "POST")
            {
                await WriteJson(resp, new { ok = true });
                _ = Task.Run(async () =>
                {
                    await Task.Delay(250);
                    System.Windows.Forms.Application.Exit();
                });
            }
            else
            {
                resp.StatusCode = 404;
                resp.Close();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Request handler error: {ex.Message}");
            try
            {
                resp.StatusCode = 500;
                resp.Close();
            }
            catch { }
        }
    }

    private async Task ServeHtml(HttpListenerResponse resp)
    {
        var html = _settingsHtml ?? "<h1>Settings page not found</h1>";
        var buffer = Encoding.UTF8.GetBytes(html);
        resp.ContentType = "text/html; charset=utf-8";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private async Task ServeSettings(HttpListenerResponse resp)
    {
        // TTS info is Windows-only (SAPI/Piper); give null on non-Windows to keep the API shape.
        var ttsBackend = OperatingSystem.IsWindows() ? TtsService.Instance.ActiveBackend.ToString() : "None";

        var settings = new
        {
            speechEngine = KeyboardWtfState.SelectedEngine.ToString(),
            language = KeyboardWtfState.SelectedLanguage,
            microphoneDevice = KeyboardWtfState.SelectedMicrophoneIndex,
            maxRecordingSeconds = KeyboardWtfState.MaxRecordingSeconds,
            whisperModelSize = KeyboardWtfState.SelectedWhisperModel.ToString(),
            voskLoaded = _app?.SpeechRecognition?.IsVoskLoaded ?? false,
            whisperLoaded = _app?.SpeechRecognition?.IsWhisperLoaded ?? false,
            voskPresent = ModelManager.IsVoskPresent,
            whisperPresent = ModelManager.IsWhisperPresent,
            modelsPath = ModelManager.ModelsBaseDir,
            slackWebhookUrl = MaskWebhookUrl(KeyboardWtfState.SlackWebhookUrl),
            slackConfigured = !string.IsNullOrEmpty(KeyboardWtfState.SlackWebhookUrl),
            discordWebhookUrl = MaskWebhookUrl(KeyboardWtfState.DiscordWebhookUrl),
            discordConfigured = !string.IsNullOrEmpty(KeyboardWtfState.DiscordWebhookUrl),
            voiceNoteSavePath = KeyboardWtfState.VoiceNoteSavePath ?? "",
            voiceNoteEffectivePath = KeyboardWtfState.EffectiveVoiceNoteSavePath,
            voiceNoteFilenamePattern = KeyboardWtfState.VoiceNoteFilenamePattern ?? "",
            voiceNoteDefaultPattern = KeyboardWtfState.DefaultVoiceNoteFilenamePattern,
            voiceNoteFilenamePreview = PreviewFilename(KeyboardWtfState.VoiceNoteFilenamePattern ?? KeyboardWtfState.DefaultVoiceNoteFilenamePattern),

            aiProvider = AiProvider.Gemini.ToString(),
            geminiApiKey = MaskApiKey(KeyboardWtfState.GeminiApiKey),
            geminiConfigured = !string.IsNullOrEmpty(KeyboardWtfState.GeminiApiKey),
            bridgePairingToken = KeyboardWtfState.BridgePairingToken,
            bridgePort = Port,
            bridgeDashboardOrigin = AllowedBridgeOrigin(),
            bridgeActions = LocalBridgeActionRegistry.All,

            // Noise gate + transcript post-processing
            useNoiseGate = KeyboardWtfState.UseNoiseGate,
            translateTargetLanguage = KeyboardWtfState.TranslateTargetLanguage ?? "",
            useFillerWordCleaner = KeyboardWtfState.UseFillerWordCleaner,
            caseTransform = KeyboardWtfState.SelectedCaseTransform.ToString(),

            // AI toggle + custom prompts
            useAi = KeyboardWtfState.UseAi,
            customPromptEmail = KeyboardWtfState.CustomPrompts.TryGetValue("Email", out var cpEmail) ? cpEmail : "",
            customPromptSlack = KeyboardWtfState.CustomPrompts.TryGetValue("Slack", out var cpSlack) ? cpSlack : "",
            customPromptDiscord = KeyboardWtfState.CustomPrompts.TryGetValue("Discord", out var cpDiscord) ? cpDiscord : "",
            customPromptTeams = KeyboardWtfState.CustomPrompts.TryGetValue("Teams", out var cpTeams) ? cpTeams : "",
            customPromptCalendar = KeyboardWtfState.CustomPrompts.TryGetValue("Calendar", out var cpCal) ? cpCal : "",

            // Text-to-Speech
            ttsActiveBackend = ttsBackend,
            ttsPath = PiperInstaller.ActivePiperDir ?? PiperInstaller.TtsRoot,
            ttsBundledPath = PiperInstaller.BundledTtsRoot,
            piperBinaryPresent = PiperInstaller.IsBinaryPresent,
            piperEnVoicePresent = PiperInstaller.IsEnVoicePresent,
            piperDeVoicePresent = PiperInstaller.IsDeVoicePresent,
            piperBinarySource = PiperInstaller.PiperBinarySource,       // "Bundled" | "Downloaded" | "None"
            piperEnVoiceSource = PiperInstaller.EnVoiceSource,
            piperDeVoiceSource = PiperInstaller.DeVoiceSource,
            piperFullyInstalled = PiperInstaller.IsFullyInstalled,
            isInstallingPiper = KeyboardWtfState.IsInstallingPiper,
            piperInstallStatus = KeyboardWtfState.PiperInstallStatus,

            hotkeys = _settings?.Current.Hotkeys ?? HotkeySettings.Defaults(),
            defaultDestination = _settings?.Current.DefaultDestination ?? "Clipboard",
            textDeliveryMode = (_settings?.Current.TextDeliveryMode ?? VoiceTextDeliveryMode.TypeIntoActiveApp).ToString(),
            assistantName = KeyboardWtfState.AssistantName,
            assistantTone = KeyboardWtfState.AssistantTone.ToString(),
            geminiVoice = KeyboardWtfState.GeminiVoice,
            geminiVoices = SettingsService.GeminiVoices.Select(pair => new { name = pair.Key, style = pair.Value }).ToArray(),
            startWithWindows = _settings?.Current.StartWithWindows ?? true,
            startupExecutablePath = _app?.StartupExecutablePath ?? "",
            startupRegistered = WindowsStartupService.IsRegisteredFor(_app?.StartupExecutablePath ?? ""),
            startupRegisteredCommand = WindowsStartupService.RegisteredCommand(),
            jarvisPermissionMode = (_settings?.Current.JarvisPermissionMode ?? JarvisPermissionMode.AlwaysAsk).ToString(),
            intentMemory = _intentMemory?.Snapshot() ?? new IntentMemorySnapshot(),
            jarvisWorkflows = _settings?.Current.JarvisWorkflows ?? new List<JarvisWorkflowSettings>(),
            jarvisActionHistory = _jarvisHistory?.Snapshot() ?? Array.Empty<JarvisActionEntry>(),
            pendingSensitiveAction = _app?.Commands?.PendingSensitiveAction
        };

        var json = JsonSerializer.Serialize(settings);
        var buffer = Encoding.UTF8.GetBytes(json);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private async Task SaveSettings(HttpListenerRequest req, HttpListenerResponse resp)
    {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        var body = await reader.ReadToEndAsync();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("speechEngine", out var engineEl))
        {
            if (Enum.TryParse<SpeechEngine>(engineEl.GetString(), out var engine))
                _settings?.SaveSpeechEngine(engine);
        }

        if (root.TryGetProperty("language", out var langEl))
        {
            var lang = langEl.GetString();
            if (!string.IsNullOrEmpty(lang))
                _settings?.SaveLanguage(lang);
        }

        if (root.TryGetProperty("microphoneDevice", out var micEl))
        {
            var micIndex = micEl.GetInt32();
            _settings?.SaveMicrophoneDevice(micIndex);
        }

        if (root.TryGetProperty("maxRecordingSeconds", out var maxRecEl))
        {
            var maxRec = maxRecEl.GetInt32();
            if (maxRec is >= 5 and <= 300)
                _settings?.SaveMaxRecordingSeconds(maxRec);
        }

        if (root.TryGetProperty("whisperModelSize", out var modelSizeEl))
        {
            if (Enum.TryParse<WhisperModelSize>(modelSizeEl.GetString(), out var modelSize))
                _settings?.SaveWhisperModelSize(modelSize);
        }

        if (root.TryGetProperty("aiProvider", out var providerEl))
        {
            _settings?.SaveAiProvider(AiProvider.Gemini);
        }

        if (root.TryGetProperty("geminiApiKey", out var geminiEl))
        {
            var key = geminiEl.GetString();
            if (!string.IsNullOrEmpty(key) && !key.StartsWith("••••"))
                _settings?.SaveGeminiApiKey(key);
        }

        if (root.TryGetProperty("slackWebhookUrl", out var slackEl))
        {
            var url = slackEl.GetString();
            if (url != null && !url.StartsWith("••••"))
                _settings?.SaveSlackWebhookUrl(url);
        }

        if (root.TryGetProperty("discordWebhookUrl", out var discordEl))
        {
            var url = discordEl.GetString();
            if (url != null && !url.StartsWith("••••"))
                _settings?.SaveDiscordWebhookUrl(url);
        }

        if (root.TryGetProperty("voiceNoteSavePath", out var vnPathEl))
        {
            _settings?.SaveVoiceNoteSavePath(vnPathEl.GetString() ?? "");
        }

        if (root.TryGetProperty("voiceNoteFilenamePattern", out var vnPatternEl))
        {
            _settings?.SaveVoiceNoteFilenamePattern(vnPatternEl.GetString() ?? "");
        }

        if (root.TryGetProperty("useNoiseGate", out var useNgEl))
            _settings?.SaveUseNoiseGate(useNgEl.GetBoolean());
        if (root.TryGetProperty("translateTargetLanguage", out var translateEl))
            _settings?.SaveTranslateTargetLanguage(translateEl.GetString() ?? "");
        if (root.TryGetProperty("useFillerWordCleaner", out var fillerEl))
            _settings?.SaveUseFillerWordCleaner(fillerEl.GetBoolean());
        if (root.TryGetProperty("caseTransform", out var caseEl)
            && Enum.TryParse<Models.CaseTransform>(caseEl.GetString(), out var caseVal))
            _settings?.SaveCaseTransform(caseVal);

        if (root.TryGetProperty("useAi", out var useAiEl))
        {
            _settings?.SaveUseAi(useAiEl.GetBoolean());
        }

        if (root.TryGetProperty("assistantName", out var assistantNameEl))
            _settings?.SaveAssistantName(assistantNameEl.GetString() ?? "Jarvis");

        if (root.TryGetProperty("assistantTone", out var assistantToneEl)
            && Enum.TryParse<AssistantTone>(assistantToneEl.GetString(), out var assistantTone))
            _settings?.SaveAssistantTone(assistantTone);
        if (root.TryGetProperty("geminiVoice", out var geminiVoiceEl))
            _settings?.SaveGeminiVoice(geminiVoiceEl.GetString() ?? "Kore");
        if (root.TryGetProperty("startWithWindows", out var startupEl))
        {
            _settings?.SaveStartWithWindows(startupEl.GetBoolean());
            _app?.SyncStartupRegistration();
        }
        if (root.TryGetProperty("jarvisPermissionMode", out var permissionEl)
            && Enum.TryParse<JarvisPermissionMode>(permissionEl.GetString(), out var permissionMode))
            _settings?.SaveJarvisPermissionMode(permissionMode);

        if (root.TryGetProperty("jarvisWorkflow", out var workflowEl))
        {
            _settings?.SaveWorkflow(
                GetJsonString(workflowEl, "name"),
                GetJsonString(workflowEl, "apps"),
                GetJsonString(workflowEl, "urls"),
                GetJsonString(workflowEl, "folder"));
        }

        foreach (var destName in new[] { "Email", "Slack", "Discord", "Teams", "Calendar" })
        {
            var propKey = "customPrompt" + destName;
            if (root.TryGetProperty(propKey, out var promptEl))
                _settings?.SaveCustomPrompt(destName, promptEl.GetString() ?? "");
        }

        if (root.TryGetProperty("defaultDestination", out var defaultDestEl))
            _settings?.SaveDefaultDestination(defaultDestEl.GetString() ?? "Clipboard");

        if (root.TryGetProperty("textDeliveryMode", out var deliveryEl)
            && Enum.TryParse<VoiceTextDeliveryMode>(deliveryEl.GetString(), out var deliveryMode))
            _settings?.SaveTextDeliveryMode(deliveryMode);

        if (root.TryGetProperty("hotkeys", out var hotkeysEl))
        {
            var hotkeys = new HotkeySettings
            {
                PushToTalk = GetHotkey(hotkeysEl, "pushToTalk", _settings?.Current.Hotkeys.PushToTalk ?? "Ctrl+Alt+K"),
                Dictation = GetHotkey(hotkeysEl, "dictation", _settings?.Current.Hotkeys.Dictation ?? "Ctrl+Alt+D"),
                Jarvis = GetHotkey(hotkeysEl, "jarvis",
                    GetHotkey(hotkeysEl, "quickSend", _settings?.Current.Hotkeys.Jarvis ?? "Ctrl+Alt+Q")),
                CommandMode = "",
                QuickSend = "",
                Cancel = GetHotkey(hotkeysEl, "cancel", _settings?.Current.Hotkeys.Cancel ?? "Ctrl+Alt+X"),
                OpenSettings = GetHotkey(hotkeysEl, "openSettings", _settings?.Current.Hotkeys.OpenSettings ?? "Ctrl+Alt+,"),
            };

            if (HotkeyService.HasDuplicates(hotkeys, out var duplicate))
            {
                resp.StatusCode = 400;
                var error = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { ok = false, error = $"Duplicate hotkey: {duplicate}" }));
                resp.ContentType = "application/json";
                resp.ContentLength64 = error.Length;
                await resp.OutputStream.WriteAsync(error);
                resp.Close();
                return;
            }

            _settings?.SaveHotkeys(hotkeys);
            _app?.RegisterHotkeys();
        }

        var buffer = Encoding.UTF8.GetBytes("{\"ok\":true}");
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private static async Task WriteJson(HttpListenerResponse resp, object payload)
    {
        var buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private static string GetHotkey(JsonElement element, string property, string fallback)
    {
        if (!element.TryGetProperty(property, out var value))
            return fallback;

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
    }

    private async Task DeleteIntentMemory(HttpListenerRequest req, HttpListenerResponse resp)
    {
        try
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            using var doc = JsonDocument.Parse(body);
            var id = doc.RootElement.TryGetProperty("id", out var idEl)
                ? idEl.GetString()
                : doc.RootElement.TryGetProperty("key", out var keyEl)
                    ? keyEl.GetString()
                    : "";
            var removed = _intentMemory?.Forget(id) ?? false;
            await WriteJson(resp, new { ok = true, removed });
        }
        catch (Exception ex)
        {
            await WriteJson(resp, new { ok = false, error = ex.Message });
        }
    }

    private async Task RememberIntentMemory(HttpListenerRequest req, HttpListenerResponse resp)
    {
        try
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            using var doc = JsonDocument.Parse(body);
            var key = doc.RootElement.TryGetProperty("key", out var keyEl) ? keyEl.GetString() : "";
            var value = doc.RootElement.TryGetProperty("value", out var valueEl)
                ? valueEl.GetString()
                : doc.RootElement.TryGetProperty("text", out var textEl)
                    ? textEl.GetString()
                    : "";
            var entry = _intentMemory?.Remember(key, value);
            await WriteJson(resp, new { ok = entry != null, entry });
        }
        catch (Exception ex)
        {
            resp.StatusCode = 400;
            await WriteJson(resp, new { ok = false, error = ex.Message });
        }
    }

    private async Task DeleteJarvisWorkflow(HttpListenerRequest req, HttpListenerResponse resp)
    {
        try
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            using var doc = JsonDocument.Parse(body);
            var name = GetJsonString(doc.RootElement, "name");
            var removed = _settings?.DeleteWorkflow(name) ?? false;
            await WriteJson(resp, new
            {
                ok = removed,
                removed,
                error = removed ? null : "Workflow not found.",
            });
        }
        catch (Exception ex)
        {
            resp.StatusCode = 400;
            await WriteJson(resp, new { ok = false, error = ex.Message });
        }
    }

    private static string GetJsonString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value.GetString() ?? ""
            : "";

    private async Task ServeMicrophones(HttpListenerResponse resp)
    {
        var devices = new List<object>();
        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            devices.Add(new { index = i, name = caps.ProductName, channels = caps.Channels });
        }

        var json = JsonSerializer.Serialize(new
        {
            devices,
            selected = KeyboardWtfState.SelectedMicrophoneIndex
        });
        var buffer = Encoding.UTF8.GetBytes(json);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private async Task RunMicTest(HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            var format = new WaveFormat(16000, 1);
            var deviceIndex = KeyboardWtfState.SelectedMicrophoneIndex;
            var waveIn = new WaveInEvent
            {
                DeviceNumber = deviceIndex,
                WaveFormat = format,
                BufferMilliseconds = 50
            };

            var pcm = new MemoryStream();
            waveIn.DataAvailable += (s, e) => pcm.Write(e.Buffer, 0, e.BytesRecorded);

            waveIn.StartRecording();
            await Task.Delay(2000);
            waveIn.StopRecording();
            waveIn.Dispose();

            var pcmData = pcm.ToArray();
            pcm.Dispose();

            // Calculate peak amplitude from raw PCM (16-bit samples)
            var peak = 0;
            for (var i = 0; i < pcmData.Length - 1; i += 2)
            {
                var sample = Math.Abs((short)(pcmData[i] | (pcmData[i + 1] << 8)));
                if (sample > peak) peak = sample;
            }

            var peakDb = peak > 0 ? 20.0 * Math.Log10(peak / 32768.0) : -96.0;
            var byteCount = pcmData.Length;

            resultJson = JsonSerializer.Serialize(new
            {
                ok = true,
                durationMs = 2000,
                peakDb = Math.Round(peakDb, 1),
                bytesRecorded = byteCount,
                detected = peak > 500
            });
        }
        catch (Exception ex)
        {
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }

        var buffer = Encoding.UTF8.GetBytes(resultJson);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private async Task InstallPiper(HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            // Kick off install on a background task; don't block the HTTP response waiting for
            // ~150 MB of downloads. UI polls /api/settings for status.
            if (!KeyboardWtfState.IsInstallingPiper)
            {
                await PiperInstaller.EnsureInstalledAsync(status =>
                {
                    KeyboardWtfState.PiperInstallStatus = status;
                    AppLog.Info($"Piper install: {status}");
                });
            }
            resultJson = JsonSerializer.Serialize(new { ok = true, installing = true });
        }
        catch (Exception ex)
        {
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }

        var buffer = Encoding.UTF8.GetBytes(resultJson);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private async Task TestTts(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                resultJson = JsonSerializer.Serialize(new { ok = false, error = "TTS is Windows-only" });
            }
            else
            {
                // Optional {"lang": "en" | "de"} body to force a specific voice. Omitted = use current language setting.
                string langOverride = null;
                try
                {
                    using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                    var body = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        using var doc = JsonDocument.Parse(body);
                        if (doc.RootElement.TryGetProperty("lang", out var langEl))
                            langOverride = langEl.GetString();
                    }
                }
                catch { /* empty body is fine */ }

                var effective = string.IsNullOrEmpty(langOverride) ? (KeyboardWtfState.SelectedLanguage ?? "auto") : langOverride;
                var sample = effective == "de"
                    ? "Hallo, dies ist ein Test von keyboard.wtf."
                    : "Hello, this is a keyboard.wtf voice test.";
                TtsService.Instance.Speak(sample, langOverride);
                resultJson = JsonSerializer.Serialize(new
                {
                    ok = true,
                    backend = TtsService.Instance.ActiveBackend.ToString(),
                    language = effective
                });
            }
        }
        catch (Exception ex)
        {
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }

        var buffer = Encoding.UTF8.GetBytes(resultJson);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private async Task DownloadModels(HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            await ModelManager.EnsureModelsAsync(status => AppLog.Info($"Model download: {status}"));

            // Reload models into the speech service
            if (ModelManager.IsVoskPresent && !(_app?.SpeechRecognition?.IsVoskLoaded ?? false))
                _app?.SpeechRecognition?.LoadVoskModel(ModelManager.VoskModelPath);

            if (ModelManager.IsWhisperPresent && !(_app?.SpeechRecognition?.IsWhisperLoaded ?? false))
                _app?.SpeechRecognition?.LoadWhisperModel(ModelManager.WhisperModelPath);

            resultJson = JsonSerializer.Serialize(new
            {
                ok = true,
                voskPresent = ModelManager.IsVoskPresent,
                whisperPresent = ModelManager.IsWhisperPresent,
                voskLoaded = _app?.SpeechRecognition?.IsVoskLoaded ?? false,
                whisperLoaded = _app?.SpeechRecognition?.IsWhisperLoaded ?? false
            });
        }
        catch (Exception ex)
        {
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }

        var buffer = Encoding.UTF8.GetBytes(resultJson);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    /// <summary>
    /// Smoke test for the Gemini-only hackathon AI provider.
    /// </summary>
    private async Task TestAiProvider(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            var provider = Services.Ai.AiProviderRegistry.Get(AiProvider.Gemini);
            if (!provider.IsAvailable)
            {
                resultJson = JsonSerializer.Serialize(new { ok = false, error = "Google Gemini API key not configured" });
            }
            else
            {
                var (success, message) = await provider.TestApiKeyAsync();
                resultJson = JsonSerializer.Serialize(new { ok = success, message });
            }
        }
        catch (Exception ex)
        {
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }

        var buffer = Encoding.UTF8.GetBytes(resultJson);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private async Task ServeBridgeStatus(HttpListenerRequest req, HttpListenerResponse resp)
    {
        if (!IsBridgeAuthorized(req))
        {
            resp.StatusCode = 401;
            await WriteJson(resp, new { ok = false, error = "Pairing token required." });
            return;
        }

        await WriteJson(resp, new
        {
            ok = true,
            online = true,
            service = "keyboard.wtf local desktop bridge",
            port = Port,
            device_id = Environment.GetEnvironmentVariable("DEFAULT_DEVICE_ID") ?? "tanush-windows-demo",
            actions = LocalBridgeActionRegistry.All,
        });
    }

    private async Task ExecuteBridgeAction(HttpListenerRequest req, HttpListenerResponse resp)
    {
        if (!IsBridgeAuthorized(req))
        {
            resp.StatusCode = 401;
            await WriteJson(resp, new { ok = false, error = "Pairing token required." });
            return;
        }

        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        var body = await reader.ReadToEndAsync();
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = document.RootElement;
        var action = root.TryGetProperty("action", out var actionElement)
            ? actionElement.GetString()?.Trim()
            : "";
        var parameters = root.TryGetProperty("params", out var paramsElement)
            ? paramsElement
            : JsonSerializer.SerializeToElement(new { });
        var confirmed = root.TryGetProperty("confirmed", out var confirmedElement)
            && confirmedElement.ValueKind == JsonValueKind.True;

        if (!LocalBridgeActionRegistry.TryResolve(action, parameters, out var definition, out var toolArguments))
        {
            resp.StatusCode = 400;
            await WriteJson(resp, new { ok = false, error = "Action is not in the local bridge allowlist." });
            return;
        }

        if (definition.RequiresConfirmation && !confirmed)
        {
            resp.StatusCode = 409;
            await WriteJson(resp, new
            {
                ok = false,
                confirmation_required = true,
                safety_level = definition.SafetyLevel,
                message = $"Confirm {definition.Name} before execution.",
            });
            return;
        }

        var result = confirmed
            ? await _app.Commands.ExecuteApprovedBridgeToolAsync(definition.ToolName, toolArguments, CancellationToken.None)
            : await _app.Commands.ExecuteJarvisToolAsync(definition.ToolName, toolArguments, CancellationToken.None);
        var success = result.TryGetValue("ok", out var okValue) && okValue is bool ok && ok;
        resp.StatusCode = success ? 200 : 409;
        await WriteJson(resp, result);
    }

    private static bool IsBridgeAuthorized(HttpListenerRequest req)
    {
        var expected = KeyboardWtfState.BridgePairingToken;
        if (string.IsNullOrWhiteSpace(expected))
            return false;
        var authorization = req.Headers["Authorization"] ?? "";
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            && string.Equals(authorization[7..].Trim(), expected, StringComparison.Ordinal);
    }

    private static bool ApplyBridgeCors(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var origin = req.Headers["Origin"];
        if (string.IsNullOrWhiteSpace(origin))
            return true;

        var allowed = AllowedBridgeOrigin();
        if (!string.Equals(origin, allowed, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(origin, "https://keyboard-wtf-agent-866230084016.asia-south1.run.app", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(origin, "https://keyboard-wtf-agent-ivflfs5pta-el.a.run.app", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(origin, "http://localhost:8080", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(origin, "http://localhost:3000", StringComparison.OrdinalIgnoreCase))
            return false;

        resp.Headers["Access-Control-Allow-Origin"] = origin;
        resp.Headers["Vary"] = "Origin";
        resp.Headers["Access-Control-Allow-Headers"] = "Authorization, Content-Type";
        resp.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        resp.Headers["Access-Control-Allow-Private-Network"] = "true";
        return true;
    }

    private static string AllowedBridgeOrigin() =>
        Environment.GetEnvironmentVariable("WEB_DASHBOARD_ORIGIN")?.Trim()
        ?? "http://localhost:8080";

    private async Task StartLiveMicTest(HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            StopLiveMicInternal(); // idempotent: stop any existing session first

            var deviceIndex = KeyboardWtfState.SelectedMicrophoneIndex >= 0 ? KeyboardWtfState.SelectedMicrophoneIndex : 0;
            lock (_liveMicLock)
            {
                _liveMicPeak = 0;
                _liveMic = new WaveInEvent
                {
                    DeviceNumber = deviceIndex,
                    WaveFormat = new WaveFormat(16000, 1),
                    BufferMilliseconds = 30
                };
                _liveMic.DataAvailable += OnLiveMicData;
                _liveMic.StartRecording();
            }

            resultJson = JsonSerializer.Serialize(new { ok = true });
        }
        catch (Exception ex)
        {
            StopLiveMicInternal();
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
            AppLog.Error($"Mic test start failed: {ex.Message}");
        }

        await WriteJson(resp, resultJson);
    }

    private void OnLiveMicData(object sender, WaveInEventArgs e)
    {
        var peak = 0;
        for (var i = 0; i < e.BytesRecorded - 1; i += 2)
        {
            var sample = Math.Abs((short)(e.Buffer[i] | (e.Buffer[i + 1] << 8)));
            if (sample > peak) peak = sample;
        }
        // Keep the highest peak observed since last poll (simple VU-meter behavior)
        if (peak > _liveMicPeak)
            _liveMicPeak = peak;
    }

    private async Task GetLiveMicLevel(HttpListenerResponse resp)
    {
        var active = _liveMic != null;
        // Read-and-reset so each poll reflects activity since last call (bouncy meter)
        var peak = System.Threading.Interlocked.Exchange(ref _liveMicPeak, 0);
        var peakDb = peak > 0 ? 20.0 * Math.Log10(peak / 32768.0) : -96.0;

        var json = JsonSerializer.Serialize(new
        {
            ok = active,
            peak,
            peakDb = Math.Round(peakDb, 1),
            detected = peak > 500
        });

        await WriteJson(resp, json);
    }

    private async Task StopLiveMicTest(HttpListenerResponse resp)
    {
        StopLiveMicInternal();
        await WriteJson(resp, "{\"ok\":true}");
    }

    private void StopLiveMicInternal()
    {
        lock (_liveMicLock)
        {
            if (_liveMic == null) return;
            try
            {
                _liveMic.DataAvailable -= OnLiveMicData;
                _liveMic.StopRecording();
                _liveMic.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error($"Mic test stop: {ex.Message}");
            }
            finally
            {
                _liveMic = null;
                _liveMicPeak = 0;
            }
        }
    }

    private static async Task WriteJson(HttpListenerResponse resp, string json)
    {
        var buffer = Encoding.UTF8.GetBytes(json);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private async Task PreviewFilenameEndpoint(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            using var doc = JsonDocument.Parse(body);
            var pattern = doc.RootElement.TryGetProperty("pattern", out var p) ? p.GetString() : null;
            resultJson = JsonSerializer.Serialize(new { ok = true, preview = PreviewFilename(pattern) });
        }
        catch (Exception ex)
        {
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }

        var buffer = Encoding.UTF8.GetBytes(resultJson);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private static string PreviewFilename(string pattern)
    {
        var effective = string.IsNullOrWhiteSpace(pattern) ? KeyboardWtfState.DefaultVoiceNoteFilenamePattern : pattern;
        string raw;
        try { raw = DateTime.Now.ToString(effective); }
        catch { return "(invalid format)"; }

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(raw.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c));
        return (string.IsNullOrWhiteSpace(sanitized) ? DateTime.Now.ToString(KeyboardWtfState.DefaultVoiceNoteFilenamePattern) : sanitized) + ".wav";
    }

    private async Task PickVoiceNotesFolder(HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            var currentPath = (KeyboardWtfState.EffectiveVoiceNoteSavePath ?? "").Replace("'", "''");

            // Build the PowerShell script. Single-quoted PS strings: inner apostrophes are doubled above.
            var script =
                "Add-Type -AssemblyName System.Windows.Forms | Out-Null\n" +
                "$f = New-Object System.Windows.Forms.FolderBrowserDialog\n" +
                "$f.Description = 'Select Voice Notes save folder'\n" +
                "$f.UseDescriptionForTitle = $true\n" +
                $"$f.SelectedPath = '{currentPath}'\n" +
                "$f.ShowNewFolderButton = $true\n" +
                "if ($f.ShowDialog() -eq 'OK') { [Console]::Out.WriteLine($f.SelectedPath) }\n";

            // Base64 UTF-16LE encode the script to bypass all command-line quoting rules.
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            var psExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(psExe)) psExe = "powershell.exe";

            var psi = new ProcessStartInfo
            {
                FileName = psExe,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -STA -EncodedCommand {encoded}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            AppLog.Info($"FolderPicker: launching {psExe}");
            using var proc = Process.Start(psi);
            if (proc == null) throw new InvalidOperationException("Failed to start folder picker");

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await Task.Run(() => proc.WaitForExit(120000));
            var stdout = (await stdoutTask).Trim();
            var stderr = (await stderrTask).Trim();

            AppLog.Info($"FolderPicker exit={proc.ExitCode} stdout='{stdout}' stderr='{stderr}'");

            if (!string.IsNullOrWhiteSpace(stdout) && Directory.Exists(stdout))
            {
                _settings?.SaveVoiceNoteSavePath(stdout);
                resultJson = JsonSerializer.Serialize(new { ok = true, path = stdout });
            }
            else if (!string.IsNullOrWhiteSpace(stderr))
            {
                resultJson = JsonSerializer.Serialize(new { ok = false, error = stderr });
            }
            else
            {
                resultJson = JsonSerializer.Serialize(new { ok = false, cancelled = true });
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"PickVoiceNotesFolder failed: {ex.Message}");
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }

        await WriteJson(resp, resultJson);
    }

    private async Task OpenVoiceNotesFolder(HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            var folder = KeyboardWtfState.EffectiveVoiceNoteSavePath;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            resultJson = JsonSerializer.Serialize(new { ok = true, path = folder });
        }
        catch (Exception ex)
        {
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }

        var buffer = Encoding.UTF8.GetBytes(resultJson);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private static string MaskApiKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (key.Length <= 8) return "••••";
        return "••••" + key[^4..];
    }

    private static string MaskWebhookUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        if (url.Length <= 20) return "••••";
        return url[..20] + "••••";
    }

    private string LoadSettingsHtml()
    {
        try
        {
            return AppResources.ReadTextFile("settings.html");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load settings.html: {ex.Message}");
            return null;
        }
    }

    private int FindAvailablePort(int startPort)
    {
        for (var port = startPort; port < startPort + 200; port++)
        {
            if (port < 1024) continue;
            try
            {
                using var probe = new HttpListener();
                probe.Prefixes.Add($"http://localhost:{port}/");
                probe.Start();
                probe.Stop();
                return port;
            }
            catch { }
        }

        AppLog.Warning($"No free port found from {startPort}");
        return startPort;
    }

    private static int ResolveBridgePort()
    {
        var configured = Environment.GetEnvironmentVariable("LOCAL_BRIDGE_PORT");
        return int.TryParse(configured, out var port) && port is >= 1024 and <= 65535
            ? port
            : 8787;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopLiveMicInternal();
        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();

        try { _serverTask?.Wait(TimeSpan.FromSeconds(3)); }
        catch { }

        _cts?.Dispose();
        IsRunning = false;

        AppLog.Info("WebSettingsService stopped");
    }
}
