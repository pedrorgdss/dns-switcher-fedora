using Gtk;
using static DnsSwitcher.Fedora.Resources.Strings;

namespace DnsSwitcher.Fedora.UI;

internal static class Parts
{
    public static Box Column(int spacing = 10) => new(Orientation.Vertical, spacing);
    public static Box Row(params Widget[] widgets)
    {
        var box = new Box(Orientation.Horizontal, 8);
        foreach (var child in widgets) box.PackStart(child, child.Hexpand, child.Hexpand, 0);
        return box;
    }
    public static Label Caption(string text) => new(text) { Xalign = 0, LineWrap = true };
    public static Box Field(string key, Widget value)
    {
        var box = Column(5);
        var label = Caption(T(key)); label.StyleContext.AddClass("field-name");
        label.MnemonicWidget = value; value.Accessible.Name = T(key);
        box.PackStart(label, false, false, 0); box.PackStart(value, false, false, 0);
        return box;
    }
    public static Label Title(string key, string style = "page-title")
    {
        var label = Caption(T(key)); label.StyleContext.AddClass(style); return label;
    }
    public static Box Card(int spacing = 14)
    {
        var box = Column(spacing); box.StyleContext.AddClass("dns-card");
        return box;
    }
    public static Box Inset(Box card, int spacing = 14)
    {
        var body = Column(spacing); body.BorderWidth = 18;
        card.PackStart(body, true, true, 0); return body;
    }
    public static Image Symbol(string name, int pixels = 16) =>
        new() { IconName = name, PixelSize = pixels };
    public static Button Button(string key, System.Action clicked, bool primary = false)
    {
        var b = new Button(T(key)); b.StyleContext.AddClass("action");
        if (primary) b.StyleContext.AddClass("apply-action");
        b.Clicked += (_, _) => clicked(); return b;
    }
    public static ScrolledWindow Table(string[] keys, out ListStore model, out TreeView view)
    {
        model = new ListStore(keys.Select(_ => typeof(string)).ToArray());
        view = new TreeView(model) { HeadersVisible = true, EnableSearch = false };
        for (int i = 0; i < keys.Length; i++)
        {
            var column = view.AppendColumn(T(keys[i]), new CellRendererText { Ypad = 9, Xpad = 9 }, "text", i);
            column.Resizable = true; column.Expand = true;
        }
        view.StyleContext.AddClass("result-table");
        var scroll = new ScrolledWindow { ShadowType = ShadowType.None, Vexpand = true };
        scroll.StyleContext.AddClass("result-frame");
        scroll.Add(view); return scroll;
    }
}
