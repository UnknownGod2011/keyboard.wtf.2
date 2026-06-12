namespace KeyboardWtf.Services;

using System.Reflection;
using System.Windows.Forms;
using KeyboardWtf.Destinations;
using KeyboardWtf.Models;
using KeyboardWtf.Services.Ai;

public sealed class KeyboardWtfApp : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly SettingsService _settings = new();
    private readonly AudioRecorderService _audioRecorder = new();
    private readonly SpeechRecognitionService _speechRecognition = new();
    private readonly DestinationRouter _router = new();
    private readonly HotkeyService _hotkeys = new();
    private readonly IntentMemoryService _intentMemory = new();
    private readonly JarvisActionHistoryService _jarvisHistory = new();

    private NotificationService _notifications;
    private VoiceCaptureService _capture;
    private CommandRegistry _commands;
    private VoiceOverlayForm _overlay;
    private GeminiLiveConversationService _liveConversation;
    private JarvisAutomationService _jarvisAutomation;
    private bool _disposed;

    public KeyboardWtfApp(NotifyIcon notifyIcon)
    {
        _notifyIcon = notifyIcon;
    }

    public SettingsService Settings => _settings;
    public SpeechRecognitionService SpeechRecognition => _speechRecognition;
    public CommandRegistry Commands => _commands;

    public void Start()
    {
        AppLog.Init();
        AppResources.Init(Assembly.GetExecutingAssembly());

        _notifications = new NotificationService(_notifyIcon);
        _settings.Load();
        _intentMemory.Load();
        _jarvisHistory.Load();
        SyncStartupRegistration();
        ApplySecretEnvironmentOverrides();
        AiProviderRegistry.Initialize();
        RegisterDestinations();

        _capture = new VoiceCaptureService(_audioRecorder, _speechRecognition, _notifications, _router);
        _liveConversation = new GeminiLiveConversationService(_notifications, _settings, _intentMemory);
        _jarvisAutomation = new JarvisAutomationService(
            _notifications,
            _settings,
            _jarvisHistory,
            OpenSettings);
        _commands = new CommandRegistry(
            this,
            _capture,
            _router,
            _settings,
            _notifications,
            _liveConversation,
            _jarvisAutomation,
            _jarvisHistory);
        _liveConversation.SetToolExecutor(_commands.ExecuteJarvisToolAsync);
        _liveConversation.SetUserTranscriptObserver(_commands.ObserveJarvisTranscript);
        _overlay = new VoiceOverlayForm(_audioRecorder);

        WebSettingsService.Instance.Initialize(
            this,
            _settings,
            _hotkeys,
            _intentMemory,
            _jarvisHistory);
        WebSettingsService.Instance.Start();

        _hotkeys.RegistrationFailed += (name, reason) => _notifications.Warning("Hotkey unavailable", $"{name}: {reason}");
        RegisterHotkeys();
        LoadModelsAsync();
        StartPiperInstallInBackground();
        OpenFirstRunSetup();

        _notifications.Info("keyboard.wtf", "Stop typing. Say it.");
    }

    public void RegisterHotkeys()
    {
        if (HotkeyService.HasDuplicates(_settings.Current.Hotkeys, out var duplicate))
        {
            _notifications.Warning("Duplicate hotkey", $"{duplicate} is assigned more than once. Reset or change it in settings.");
            return;
        }

        _hotkeys.RegisterDefaults(_settings.Current.Hotkeys, _commands);
    }

    public void OpenSettings() => WebSettingsService.Instance.OpenInBrowser();

    public string StartupExecutablePath => ResolveStartupExecutablePath();

    public void SyncStartupRegistration() =>
        WindowsStartupService.Sync(_settings.Current.StartWithWindows, StartupExecutablePath);

    private static void OpenFirstRunSetup()
    {
        if (!string.IsNullOrWhiteSpace(KeyboardWtfState.GeminiApiKey))
            return;

        _ = Task.Run(async () =>
        {
            await Task.Delay(900);
            WebSettingsService.Instance.OpenInBrowser("#set-api-key");
        });
    }

    public void ToggleAi()
    {
        _settings.SaveUseAi(!KeyboardWtfState.UseAi);
        _notifications.Info("AI formatting", KeyboardWtfState.UseAi ? "Enabled." : "Disabled.");
    }

    public void SetDefaultDestination(string destination)
    {
        _settings.SaveDefaultDestination(destination);
        _notifications.Info("Default destination", destination);
    }

    private static void RegisterDestinations()
    {
        if (DestinationRegistry.All.Count > 0)
            return;

        if (OperatingSystem.IsWindows())
        {
            DestinationRegistry.Register(new ClipboardDestination());
            DestinationRegistry.Register(new TypeOutDestination());
        }

        DestinationRegistry.Register(new EmailDestination());
        DestinationRegistry.Register(new SlackWebhookDestination());
        DestinationRegistry.Register(new DiscordWebhookDestination());
        DestinationRegistry.Register(new TeamsDestination());
        DestinationRegistry.Register(new CalendarDestination());
        DestinationRegistry.Register(new WhatsAppDestination());
        DestinationRegistry.Register(new NotionDestination());
        DestinationRegistry.Register(new TelegramDestination());
    }

    private void ApplySecretEnvironmentOverrides()
    {
        var geminiKey = Environment.GetEnvironmentVariable("KEYBOARD_WTF_GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(geminiKey) && string.IsNullOrWhiteSpace(KeyboardWtfState.GeminiApiKey))
        {
            _settings.SaveGeminiApiKey(geminiKey.Trim());
            _settings.SaveAiProvider(AiProvider.Gemini);
            AppLog.Info("Gemini API key imported from environment and saved encrypted");
        }
    }

    private async void LoadModelsAsync()
    {
        try
        {
            AppLog.Info($"Models directory: {ModelManager.ModelsBaseDir}");
            if (OperatingSystem.IsWindows())
            {
                var runtimeWhisperDll = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "whisper.dll");
                var whisperDll = File.Exists(runtimeWhisperDll)
                    ? runtimeWhisperDll
                    : Path.Combine(AppContext.BaseDirectory, "whisper.dll");
                if (File.Exists(whisperDll))
                    Whisper.net.LibraryLoader.RuntimeOptions.LibraryPath = whisperDll;
            }

            if (ModelManager.IsVoskPresent)
                _speechRecognition.LoadVoskModel(ModelManager.VoskModelPath);
            if (ModelManager.IsWhisperPresent)
                _speechRecognition.LoadWhisperModel(ModelManager.WhisperModelPath);

            var needsDownload = !ModelManager.IsVoskPresent || !ModelManager.IsWhisperPresent;
            if (!needsDownload)
                return;

            KeyboardWtfState.IsDownloadingModels = true;
            await ModelManager.EnsureModelsAsync(status =>
            {
                KeyboardWtfState.ModelDownloadStatus = status;
                AppLog.Info($"Model download: {status}");
            });

            if (ModelManager.IsVoskPresent && !_speechRecognition.IsVoskLoaded)
                _speechRecognition.LoadVoskModel(ModelManager.VoskModelPath);
            if (ModelManager.IsWhisperPresent && !_speechRecognition.IsWhisperLoaded)
                _speechRecognition.LoadWhisperModel(ModelManager.WhisperModelPath);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Model loading failed");
            _notifications?.Warning("Speech models", "Model loading failed. Vosk/Whisper can be retried in settings.");
        }
        finally
        {
            KeyboardWtfState.IsDownloadingModels = false;
            KeyboardWtfState.ModelDownloadStatus = null;
        }
    }

    private static void StartPiperInstallInBackground()
    {
        if (PiperInstaller.IsFullyInstalled)
            return;

        _ = Task.Run(async () =>
        {
            KeyboardWtfState.IsInstallingPiper = true;
            try
            {
                await PiperInstaller.EnsureInstalledAsync(status =>
                {
                    KeyboardWtfState.PiperInstallStatus = status;
                    AppLog.Info($"Piper install: {status}");
                });
            }
            catch (Exception ex)
            {
                AppLog.Warning(ex, "Piper install failed");
            }
            finally
            {
                KeyboardWtfState.IsInstallingPiper = false;
                KeyboardWtfState.PiperInstallStatus = null;
            }
        });
    }

    private static string ResolveStartupExecutablePath()
    {
        var current = Environment.ProcessPath ?? Application.ExecutablePath;
        if (!current.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            return current;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "dist", "keyboard-wtf-win-x64", "keyboard.wtf.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return current;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        WebSettingsService.Instance?.Dispose();
        _hotkeys.Dispose();
        _audioRecorder.Dispose();
        _speechRecognition.Dispose();
            _liveConversation?.Dispose();
        _jarvisAutomation?.Dispose();
        _overlay?.Dispose();
    }
}
