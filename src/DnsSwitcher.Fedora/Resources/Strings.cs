using System.Globalization;
using System.Resources;
using DnsSwitcher.Core.Models;

namespace DnsSwitcher.Fedora.Resources;

internal static class Strings
{
    private static readonly ResourceManager Resources = new("DnsSwitcher.Fedora.Resources.Text", typeof(Strings).Assembly);
    public static string T(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    public static string Error(Exception ex) => ex switch {
        AppError e => string.IsNullOrWhiteSpace(e.Detail) ? T(e.Key) : T(e.Key) + Environment.NewLine + e.Detail,
        UnauthorizedAccessException => T("FileAccess"),
        IOException => T("FileError") + Environment.NewLine + ex.Message,
        _ => ex.Message
    };
}
