namespace KeyboardWtf.Services.Ai;

using System.Collections.Generic;
using KeyboardWtf.Models;

/// <summary>
/// Singleton registry for the hackathon build's Gemini-only AI provider.
/// The generic interface stays so destinations and planners do not need special-case wiring.
/// </summary>
internal static class AiProviderRegistry
{
    private static readonly Dictionary<AiProvider, IAiProvider> _providers = new();
    private static IAiProvider _fallback;

    public static void Initialize()
    {
        _providers.Clear();

        var gemini = new GeminiProvider();
        _providers[AiProvider.Gemini] = gemini;
        _fallback = gemini;

        KeyboardWtfState.SelectedAiProvider = AiProvider.Gemini;
        AppLog.Info("AI provider registered: Google Gemini only");
    }

    public static IReadOnlyCollection<IAiProvider> All => _providers.Values;

    public static IAiProvider Get(AiProvider id) =>
        _providers.TryGetValue(id, out var p) ? p : _fallback;

    public static IAiProvider Current => Get(KeyboardWtfState.SelectedAiProvider);

    private static int CountImplemented()
    {
        var n = 0;
        foreach (var p in _providers.Values) if (p.IsImplemented) n++;
        return n;
    }
}
