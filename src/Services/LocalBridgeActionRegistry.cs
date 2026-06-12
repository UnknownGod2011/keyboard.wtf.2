namespace KeyboardWtf.Services;

using System.Text.Json;

public sealed record LocalBridgeActionDefinition(
    string Name,
    string ToolName,
    string SafetyLevel,
    bool RequiresConfirmation);

public static class LocalBridgeActionRegistry
{
    private static readonly IReadOnlyDictionary<string, LocalBridgeActionDefinition> Actions =
        new Dictionary<string, LocalBridgeActionDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["open_url"] = new("open_url", "open_url", "safe", false),
            ["open_app"] = new("open_app", "open_app", "safe", false),
            ["chrome_next_tab"] = new("chrome_next_tab", "browser_action", "safe", false),
            ["chrome_previous_tab"] = new("chrome_previous_tab", "browser_action", "safe", false),
            ["chrome_new_tab"] = new("chrome_new_tab", "browser_action", "safe", false),
            ["chrome_reopen_tab"] = new("chrome_reopen_tab", "browser_action", "safe", false),
            ["chrome_close_tab"] = new("chrome_close_tab", "browser_action", "confirm", true),
            ["search_google"] = new("search_google", "web_search", "safe", false),
            ["search_youtube"] = new("search_youtube", "web_search", "safe", false),
            ["open_gmail_compose"] = new("open_gmail_compose", "open_gmail_draft", "confirm", true),
            ["draft_email"] = new("draft_email", "open_gmail_draft", "confirm", true),
            ["copy_to_clipboard"] = new("copy_to_clipboard", "copy_text", "safe", false),
            ["switch_window"] = new("switch_window", "window_action", "safe", false),
            ["show_desktop"] = new("show_desktop", "window_action", "safe", false),
        };

    public static IReadOnlyCollection<LocalBridgeActionDefinition> All => Actions.Values.ToArray();

    public static bool TryResolve(
        string action,
        JsonElement parameters,
        out LocalBridgeActionDefinition definition,
        out JsonElement toolArguments)
    {
        toolArguments = default;
        if (!Actions.TryGetValue(action ?? "", out definition))
            return false;

        var values = parameters.ValueKind == JsonValueKind.Object
            ? parameters
            : JsonSerializer.SerializeToElement(new { });

        object mapped = definition.Name switch
        {
            "chrome_next_tab" => new { action = "next tab" },
            "chrome_previous_tab" => new { action = "previous tab" },
            "chrome_new_tab" => new { action = "new tab" },
            "chrome_reopen_tab" => new { action = "reopen tab" },
            "chrome_close_tab" => new { action = "close tab" },
            "search_google" => new { query = Read(values, "query"), engine = "google" },
            "search_youtube" => new { query = Read(values, "query"), engine = "youtube" },
            "open_gmail_compose" or "draft_email" => new
            {
                recipient = Read(values, "recipient"),
                subject = Read(values, "subject"),
                body = Read(values, "body"),
            },
            "copy_to_clipboard" => new { text = Read(values, "text") },
            "switch_window" => new { action = "switch", app_name = Read(values, "app_name") },
            "show_desktop" => new { action = "show desktop" },
            _ => values,
        };

        toolArguments = mapped is JsonElement element
            ? element.Clone()
            : JsonSerializer.SerializeToElement(mapped);
        return true;
    }

    private static string Read(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";
}
