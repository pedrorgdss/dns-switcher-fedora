using System.Runtime.InteropServices;
using Gtk;
using static DnsSwitcher.Fedora.Resources.Strings;

namespace DnsSwitcher.Fedora.UI;

internal sealed class Desktop : IDisposable
{
    [DllImport("libappindicator3.so.1", CharSet = CharSet.Ansi)]
    private static extern IntPtr app_indicator_new_with_path(string id, string icon, int category, string iconThemePath);
    [DllImport("libappindicator3.so.1")] private static extern void app_indicator_set_status(IntPtr obj, int status);
    [DllImport("libappindicator3.so.1")] private static extern void app_indicator_set_menu(IntPtr obj, IntPtr menu);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_object_unref(IntPtr obj);
    [DllImport("libgio-2.0.so.0")] private static extern IntPtr g_settings_schema_source_get_default();
    [DllImport("libgio-2.0.so.0")] private static extern IntPtr g_settings_schema_source_lookup(IntPtr source, string id, int recursive);
    [DllImport("libgio-2.0.so.0")] private static extern int g_settings_schema_has_key(IntPtr schema, string key);
    [DllImport("libgio-2.0.so.0")] private static extern void g_settings_schema_unref(IntPtr schema);

    private static bool HasColorScheme()
    {
        var source = g_settings_schema_source_get_default();
        var schema = source == IntPtr.Zero ? IntPtr.Zero : g_settings_schema_source_lookup(source, "org.gnome.desktop.interface", 1);
        if (schema == IntPtr.Zero) return false;
        try { return g_settings_schema_has_key(schema, "color-scheme") != 0; }
        finally { g_settings_schema_unref(schema); }
    }
    private readonly Menu menu = new();
    private IntPtr indicator;
    private DirectoryInfo? trayIcons;
    private GLib.Settings? preferences;

    public Desktop()
    {
        // GTK follows the toolkit theme. GNOME exposes its dark preference separately.
        if (HasColorScheme())
        {
            preferences = new GLib.Settings("org.gnome.desktop.interface");
            void Sync() => Gtk.Settings.Default.ApplicationPreferDarkTheme = preferences.GetString("color-scheme") == "prefer-dark";
            Sync(); preferences.Changed += (_, _) => Sync();
        }
        try
        {
            // The panel runs in another process and needs a real path to the embedded icon.
            trayIcons = Directory.CreateTempSubdirectory("dns-switcher-icons-");
            using (var source = Appearance.Resource("icon.png"))
            using (var output = File.Create(Path.Combine(trayIcons.FullName, Appearance.AppId + ".png")))
                source.CopyTo(output);
            indicator = app_indicator_new_with_path(Appearance.AppId, Appearance.AppId, 2, trayIcons.FullName);
            if (indicator != IntPtr.Zero) { app_indicator_set_menu(indicator, menu.Handle); app_indicator_set_status(indicator, 1); }
        }
        catch (DllNotFoundException) { /* Panels without AppIndicator can use the main window. */ }
    }

    public void MenuFor(MainWindow window, string? target, IEnumerable<(string Name, System.Action Run)> actions, bool busy)
    {
        foreach (var item in menu.Children) { menu.Remove(item); item.Destroy(); }
        void Add(string name, System.Action run, bool enabled = true)
        {
            var item = new MenuItem(name) { Sensitive = enabled, UseUnderline = false };
            item.Activated += (_, _) => run(); menu.Append(item);
        }
        Add(T("Open"), () => { window.Deiconify(); window.Present(); });
        if (target != null) Add(target, () => { }, false);
        foreach (var (name, run) in actions) Add(name, run, !busy);
        Add(T("Exit"), window.RequestClose);
        menu.ShowAll();
    }

    public void Dispose()
    {
        preferences?.Dispose();
        if (indicator != IntPtr.Zero) { app_indicator_set_status(indicator, 0); g_object_unref(indicator); indicator = IntPtr.Zero; }
        menu.Destroy();
        if (trayIcons != null) { File.Delete(Path.Combine(trayIcons.FullName, Appearance.AppId + ".png")); trayIcons.Delete(); }
    }
}
