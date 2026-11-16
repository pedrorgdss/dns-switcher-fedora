using DnsSwitcher.Core.Models;
using DnsSwitcher.Core.Services;
using Gtk;
using static DnsSwitcher.Fedora.Resources.Strings;

namespace DnsSwitcher.Fedora.UI;

internal sealed partial class MainWindow
{
    private ListStore providers = null!;
    private TreeView providerView = null!;

    private void BuildCatalog()
    {
        catalog.PackStart(Parts.Title("Providers"), false, false, 0);
        var actions = Parts.Row(Parts.Button("Add", () => Run(() => Edit(null)), true),
            Parts.Button("Edit", () => Run(() => Edit(SelectedProvider()))),
            Parts.Button("Remove", () => Run(Remove)));
        actions.PackEnd(Parts.Row(Parts.Button("Import", () => Run(Import)), Parts.Button("Export", () => Run(Export))),
            false, false, 0);
        catalog.PackStart(actions, false, false, 0);
        catalog.PackStart(Parts.Table(["Provider","IPv4","IPv6","Id"], out providers, out providerView), true, true, 0);
    }

    private Provider SelectedProvider()
    {
        if (!providerView.Selection.GetSelected(out var iter)) throw new AppError("SelectProvider");
        string id = (string)providers.GetValue(iter, 3);
        return config!.Providers.First(p => p.Id == id);
    }
    private void FillCatalog()
    {
        providers.Clear();
        foreach (var p in config!.Providers) providers.AppendValues(p.Name, string.Join("\n", p.Ipv4), string.Join("\n", p.Ipv6), p.Id);
    }

    private async Task Edit(Provider? previous)
    {
        using var dlg = new Dialog(T(previous == null ? "AddProvider" : "EditProvider"), this, DialogFlags.Modal,
            T("Cancel"), ResponseType.Cancel, T("Save"), ResponseType.Ok);
        var form = Parts.Column(10); form.BorderWidth = 16; dlg.ContentArea.PackStart(form, true, true, 0);
        Entry Input(string? value) => new() { Text = value ?? "", WidthChars = 22 };
        var id = Input(previous?.Id); var name = Input(previous?.Name); id.IsEditable = previous == null;
        form.PackStart(Parts.Row(Parts.Field("Provider", name), Parts.Field("Id", id)), false, false, 0);
        var v4a = Input(previous?.Ipv4.ElementAtOrDefault(0)); var v4b = Input(previous?.Ipv4.ElementAtOrDefault(1));
        var v6a = Input(previous?.Ipv6.ElementAtOrDefault(0)); var v6b = Input(previous?.Ipv6.ElementAtOrDefault(1));
        foreach (var (caption, first, second) in new[] { ("IPv4", v4a, v4b), ("IPv6", v6a, v6b) })
        {
            form.PackStart(Parts.Caption(caption), false, false, 0);
            form.PackStart(Parts.Row(Parts.Field("Primary", first), Parts.Field("Secondary", second)), false, false, 0);
        }
        var feedback = Parts.Caption(""); form.PackStart(feedback, false, false, 0);
        Provider? edited = null;
        try
        {
            dlg.ShowAll();
            while (dlg.Run() == (int)ResponseType.Ok)
            {
                try
                {
                    static string[] Pair(Entry a, Entry b) => string.IsNullOrWhiteSpace(b.Text)
                        ? string.IsNullOrWhiteSpace(a.Text) ? [] : [a.Text] : [a.Text,b.Text];
                    edited = Validation.Providers([new Provider(id.Text.Trim().ToLowerInvariant(), name.Text.Trim(),
                        Pair(v4a, v4b), Pair(v6a, v6b))])[0];
                    if (previous == null && config!.Providers.Any(p => p.Id == edited.Id))
                        throw new AppError("InvalidProvider");
                    break;
                }
                catch (AppError e) { edited = null; feedback.Text = Error(e); }
            }
        }
        finally { dlg.Destroy(); }
        if (edited == null) return;
        await Save(c => c with { Providers = previous == null ? [.. c.Providers, edited] :
            c.Providers.Any(p => p.Id == previous.Id) ? c.Providers.Select(p => p.Id == previous.Id ? edited : p).ToArray() :
            throw new AppError("UnknownProvider") });
        ChangedCatalog();
    }

    private bool Confirm(string prompt)
    {
        using var dialog = new MessageDialog(this, DialogFlags.Modal, MessageType.Question, ButtonsType.YesNo, false, "{0}", prompt);
        try { return dialog.Run() == (int)ResponseType.Yes; } finally { dialog.Destroy(); }
    }

    private async Task Remove()
    {
        var p = SelectedProvider();
        if (!Confirm(string.Format(T("RemoveConfirm"), p.Name))) return;
        await Save(c => c with { Providers = c.Providers.Where(x => x.Id != p.Id).ToArray() });
        ChangedCatalog();
    }

    private string? SelectFile(bool save)
    {
        using var dialog = new FileChooserDialog(T(save ? "Export" : "Import"), this,
            save ? FileChooserAction.Save : FileChooserAction.Open, T("Cancel"), ResponseType.Cancel,
            T(save ? "Export" : "Import"), ResponseType.Ok) { DoOverwriteConfirmation = save };
        var filter = new FileFilter { Name = "JSON" }; filter.AddPattern("*.json"); dialog.AddFilter(filter);
        if (save) dialog.CurrentName = "providers.json";
        try { return dialog.Run() == (int)ResponseType.Ok ? dialog.Filename : null; }
        finally { dialog.Destroy(); }
    }

    private async Task Import()
    {
        if (SelectFile(false) is not { } file) return;
        var items = await store.ImportAsync(file, default);
        if (!Confirm(string.Format(T("ImportConfirm"), items.Length))) return;
        await Save(c => c with { Providers = items, PreferAlternate = false }); ChangedCatalog();
    }
    private async Task Export()
    {
        if (SelectFile(true) is not { } file) return;
        await store.ExportAsync(file, config!.Providers, default); status.Text = T("Exported");
    }
    private void ChangedCatalog() { ClearResults(); status.Text = T("Saved"); }
}
