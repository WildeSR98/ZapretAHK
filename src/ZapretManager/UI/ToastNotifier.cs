namespace ZapretManager.UI;

/// <summary>Windows toast (balloon) notifications via NotifyIcon.</summary>
public static class ToastNotifier
{
    public static void Show(string title, string text, int durationMs = 5000)
    {
        try
        {
            var t = new Thread(() =>
            {
                using var icon = new System.Windows.Forms.NotifyIcon();
                icon.Icon = System.Drawing.SystemIcons.Information;
                icon.Visible = true;
                icon.BalloonTipTitle = title;
                icon.BalloonTipText = text;
                icon.ShowBalloonTip(durationMs);
                Thread.Sleep(durationMs + 1000);
                icon.Visible = false;
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }
        catch { }
    }
}
