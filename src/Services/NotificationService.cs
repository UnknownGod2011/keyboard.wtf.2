namespace KeyboardWtf.Services;

using System.Windows.Forms;

public sealed class NotificationService
{
    private readonly NotifyIcon _notifyIcon;

    public NotificationService(NotifyIcon notifyIcon) => _notifyIcon = notifyIcon;

    public void Info(string title, string message) => Show(title, message, ToolTipIcon.Info);
    public void Warning(string title, string message) => Show(title, message, ToolTipIcon.Warning);
    public void Error(string title, string message) => Show(title, message, ToolTipIcon.Error);

    private void Show(string title, string message, ToolTipIcon icon)
    {
        AppLog.Info($"{title}: {message}");
        try
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(3500);
        }
        catch
        {
            // Balloon notifications are best-effort only.
        }
    }
}
