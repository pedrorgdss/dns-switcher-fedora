using System.Text;
using DnsSwitcher.Fedora.UI;

namespace DnsSwitcher.Fedora.Services;

internal static class Launcher
{
    public static async Task RegisterAsync()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(data) || !Path.IsPathFullyQualified(data)) data = Path.Combine(home, ".local", "share");
        var apps = Directory.CreateDirectory(Path.Combine(data, "applications"));
        var icons = Directory.CreateDirectory(Path.Combine(data, "icons", "hicolor", "scalable", "apps"));
        string target = Environment.ProcessPath ?? throw new IOException("ProcessPath");
        // A stable link keeps special characters in the portable binary's path out of the Exec command.
        var links = Directory.CreateDirectory(Path.Combine(data, Appearance.AppId));
        string link = Path.Combine(links.FullName, "dnsswitcher");
        string temporary = link + "." + Guid.NewGuid().ToString("N");
        try { File.CreateSymbolicLink(temporary, target); File.Move(temporary, link, true); }
        finally { File.Delete(temporary); }
        using var image = Appearance.Resource("icon.svg");
        using var reader = new StreamReader(image);
        await ReplaceAsync(Path.Combine(icons.FullName, Appearance.AppId + ".svg"), await reader.ReadToEndAsync());
        // Path is a literal desktop-entry string. Using it as the working directory
        // also keeps percent signs in XDG_DATA_HOME out of Exec field-code expansion.
        string directory = links.FullName.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        string entry = $"""
            [Desktop Entry]
            Version=1.0
            Type=Application
            Name=DNS Switcher
            Comment=Change network DNS servers
            Comment[pt_BR]=Troque os servidores DNS da conexão
            Path={directory}
            Exec=/usr/bin/env ./dnsswitcher
            Icon={Appearance.AppId}
            StartupWMClass={Appearance.AppId}
            Terminal=false
            Categories=Network;GTK;
            """ + "\n";
        await ReplaceAsync(Path.Combine(apps.FullName, Appearance.AppId + ".desktop"), entry);
    }

    private static async Task ReplaceAsync(string path, string text)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N");
        try { await File.WriteAllTextAsync(temp, text, new UTF8Encoding(false)); File.Move(temp, path, true); }
        finally { File.Delete(temp); }
    }
}
