namespace GlassDesk.Settings;

public partial class SettingsWindow
{
    public void ShowUpdateStatus(string version, string status, bool checking = false)
    {
        ProductVersionText.Text = "GlassDesk " + version;
    }
}
