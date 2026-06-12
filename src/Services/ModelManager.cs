namespace KeyboardWtf.Services;

using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using KeyboardWtf.Models;

public static class ModelManager
{
    private const string VoskModelUrl = "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip";
    private const string VoskModelFolder = "vosk-model";
    private const string VoskModelMarker = "README";

    private const string WhisperModelFolder = "whisper-model";

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public static string ModelsBaseDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "keyboard.wtf", "models");

    public static string VoskModelPath => Path.Combine(ModelsBaseDir, VoskModelFolder);

    public static string WhisperModelPath =>
        Path.Combine(ModelsBaseDir, WhisperModelFolder, GetWhisperFileName(KeyboardWtfState.SelectedWhisperModel));

    public static bool IsVoskPresent => Directory.Exists(VoskModelPath)
        && File.Exists(Path.Combine(VoskModelPath, VoskModelMarker));

    public static bool IsWhisperPresent => IsWhisperModelValid(WhisperModelPath, KeyboardWtfState.SelectedWhisperModel);

    public static string GetWhisperFileName(WhisperModelSize size) => size switch
    {
        WhisperModelSize.Base => "ggml-base.bin",
        WhisperModelSize.Small => "ggml-small.bin",
        _ => "ggml-small.bin"
    };

    private static string GetWhisperDownloadUrl(WhisperModelSize size) => size switch
    {
        WhisperModelSize.Base => "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin",
        WhisperModelSize.Small => "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin",
        _ => "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin"
    };

    private static string GetWhisperSizeLabel(WhisperModelSize size) => size switch
    {
        WhisperModelSize.Base => "~140 MB",
        WhisperModelSize.Small => "~460 MB",
        _ => "~460 MB"
    };

    private static long GetWhisperMinimumBytes(WhisperModelSize size) => size switch
    {
        WhisperModelSize.Base => 100L * 1024 * 1024,
        WhisperModelSize.Small => 300L * 1024 * 1024,
        _ => 300L * 1024 * 1024
    };

    private static bool IsWhisperModelValid(string path, WhisperModelSize size)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length >= GetWhisperMinimumBytes(size);
        }
        catch
        {
            return false;
        }
    }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Path.Combine(ModelsBaseDir, VoskModelFolder));
        Directory.CreateDirectory(Path.Combine(ModelsBaseDir, WhisperModelFolder));
    }

    public static async Task<bool> DownloadVoskModelAsync(Action<string> onProgress = null)
    {
        if (IsVoskPresent)
        {
            AppLog.Info("Vosk model already present");
            return true;
        }

        try
        {
            EnsureDirectories();
            var zipPath = Path.Combine(ModelsBaseDir, "vosk-model-download.zip");

            onProgress?.Invoke("Downloading Vosk model (~40 MB)...");
            AppLog.Info($"Downloading Vosk model from {VoskModelUrl}");

            using (var response = await _http.GetAsync(VoskModelUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                using var fs = File.Create(zipPath);
                await response.Content.CopyToAsync(fs);
            }

            onProgress?.Invoke("Extracting Vosk model...");
            AppLog.Info("Extracting Vosk model");

            var tempExtract = Path.Combine(ModelsBaseDir, "vosk-extract-temp");
            if (Directory.Exists(tempExtract))
                Directory.Delete(tempExtract, true);

            ZipFile.ExtractToDirectory(zipPath, tempExtract);

            var extractedDirs = Directory.GetDirectories(tempExtract);
            var sourceDir = extractedDirs.Length > 0 ? extractedDirs[0] : tempExtract;

            if (Directory.Exists(VoskModelPath))
                Directory.Delete(VoskModelPath, true);

            Directory.Move(sourceDir, VoskModelPath);

            if (Directory.Exists(tempExtract))
                Directory.Delete(tempExtract, true);
            File.Delete(zipPath);

            AppLog.Info($"Vosk model installed at: {VoskModelPath}");
            onProgress?.Invoke("Vosk model ready");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Vosk model download failed: {ex.Message}");
            onProgress?.Invoke($"Download failed: {ex.Message}");
            return false;
        }
    }

    public static async Task<bool> DownloadWhisperModelAsync(Action<string> onProgress = null)
    {
        if (IsWhisperPresent)
        {
            AppLog.Info($"Whisper model already present: {WhisperModelPath}");
            return true;
        }

        var size = KeyboardWtfState.SelectedWhisperModel;
        var url = GetWhisperDownloadUrl(size);
        var sizeLabel = GetWhisperSizeLabel(size);

        try
        {
            EnsureDirectories();

            onProgress?.Invoke($"Downloading Whisper {size} model ({sizeLabel})...");
            AppLog.Info($"Downloading Whisper {size} model from {url}");

            var tempPath = WhisperModelPath + ".download";
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long contentLength
                    && contentLength < GetWhisperMinimumBytes(size))
                    throw new InvalidDataException($"Whisper download was too small ({contentLength} bytes)");

                using var fs = File.Create(tempPath);
                await response.Content.CopyToAsync(fs);
            }

            if (!IsWhisperModelValid(tempPath, size))
                throw new InvalidDataException($"Whisper download did not produce a valid {size} model");

            File.Move(tempPath, WhisperModelPath, true);
            AppLog.Info($"Whisper model installed at: {WhisperModelPath}");
            onProgress?.Invoke("Whisper model ready");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Whisper model download failed: {ex.Message}");
            onProgress?.Invoke($"Download failed: {ex.Message}");
            return false;
        }
    }

    public static async Task EnsureModelsAsync(Action<string> onProgress = null)
    {
        EnsureDirectories();

        if (!IsVoskPresent)
            await DownloadVoskModelAsync(onProgress);

        if (!IsWhisperPresent)
            await DownloadWhisperModelAsync(onProgress);
    }
}
