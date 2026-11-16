using Gtk;

namespace DnsSwitcher.Fedora.UI;

internal static class Appearance
{
    internal const string AppId = "org.dnsswitcher.DnsSwitcher";

    public static Stream Resource(string file) =>
        typeof(Appearance).Assembly.GetManifestResourceStream("DnsSwitcher.Fedora.Resources." + file)
        ?? throw new FileNotFoundException(file);

    public static void Apply()
    {
        using var stream = Resource("style.css");
        using var reader = new StreamReader(stream);
        using var provider = new CssProvider();
        provider.LoadFromData(reader.ReadToEnd());
        StyleContext.AddProviderForScreen(Gdk.Screen.Default, provider, 600);
    }

    public static Gdk.Pixbuf Picture(string file, int width, int height)
    {
        using var stream = Resource(file);
        using var source = new Gdk.Pixbuf(stream);
        return source.ScaleSimple(width, height, Gdk.InterpType.Bilinear);
    }
}
