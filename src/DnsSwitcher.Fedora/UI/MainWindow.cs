using Gtk;
using DnsSwitcher.Core.Models;
using DnsSwitcher.Core.Services;
using DnsSwitcher.Fedora.Services;
using static DnsSwitcher.Fedora.Resources.Strings;

namespace DnsSwitcher.Fedora.UI;

internal sealed partial class MainWindow : Window
{
    private readonly IConfigService store;
    private readonly IAdapterService adapters;
    private readonly IDnsService dns;
    private readonly IBenchmarkService benchmark;
    private readonly Desktop desktop = new();
    private readonly Label status = Parts.Caption("");
    private readonly Notebook tabs = new();
    private readonly Box network = Parts.Column(14), bench = Parts.Column(10), catalog = Parts.Column(10);
    private readonly ComboBoxText device = new(), provider = new(), family = new();
    private readonly Entry primary = new(), secondary = new();
    private readonly Label currentIp = Parts.Caption("—"), currentDns = Parts.Caption("—"), stateText = Parts.Caption("");
    private readonly Box stateBadge = new(Orientation.Horizontal, 5);
    private Button apply = null!, reset = null!;
    private AppConfig? config;
    private AdapterInfo[] live = [];
    private bool busy, binding, closing;
    private CancellationTokenSource? stop;
    private AdapterInfo? Selected => device.Active >= 0 && device.Active < live.Length ? live[device.Active] : null;
    private int Family => family.Active == 1 ? 6 : 4;
    private Provider? Provider => config?.Providers.FirstOrDefault(p => p.Id == provider.ActiveId);

    public MainWindow(IConfigService store, IAdapterService adapters, IDnsService dns, IBenchmarkService benchmark)
        : base("DNS Switcher")
    {
        (this.store, this.adapters, this.dns, this.benchmark) = (store, adapters, dns, benchmark);
        SetDefaultSize(828, 548);
        StyleContext.AddClass("dns-window");
        Icon = Appearance.Picture("icon.png", 128, 128);
        var heading = new HeaderBar { ShowCloseButton = true, HasSubtitle = false };
        heading.StyleContext.AddClass("dns-header");
        var brand = Parts.Row(new Image(Appearance.Picture("icon.png", 30, 30)), Parts.Caption("DNS Switcher"));
        brand.Children[1].StyleContext.AddClass("brand-name");
        heading.PackStart(brand);
        var menu = new Menu();
        var shortcut = new MenuItem(T("AddLauncher"));
        shortcut.Activated += (_, _) => Run(async () => { await Launcher.RegisterAsync(); status.Text = T("LauncherAdded"); });
        menu.Append(shortcut); menu.ShowAll();
        var options = new MenuButton { Popup = menu, Image = Parts.Symbol("open-menu-symbolic"), TooltipText = T("AppMenu") };
        options.Accessible.Name = T("AppMenu"); heading.PackEnd(options);
        // The visible title lives beside the icon; keep the shell and screen-reader title in sync.
        heading.CustomTitle = new Box(Orientation.Horizontal, 0);
        Titlebar = heading;
        Title = "DNS Switcher"; Accessible.Name = Title;
        var root = Parts.Column(0); Add(root);
        tabs.StyleContext.AddClass("dns-tabs"); tabs.ShowBorder = false;
        foreach (var (page, key, glyph) in new[] { (network, "Network", "network-wired-symbolic"),
            (bench, "Benchmark", "power-profile-performance-symbolic"), (catalog, "Providers", "view-list-symbolic") })
        {
            page.BorderWidth = 20;
            var tab = Parts.Row(Parts.Symbol(glyph), new Label(T(key))); tab.ShowAll();
            tabs.AppendPage(page, tab);
        }
        root.PackStart(tabs, true, true, 0);
        status.MarginStart = status.MarginEnd = 20; status.MarginBottom = 8;
        status.StyleContext.AddClass("status-text");
        root.PackEnd(status, false, false, 0);
        BuildNetwork(); BuildBenchmark(); BuildCatalog(); Available();
        DeleteEvent += (_, e) => { e.RetVal = true; RequestClose(); };
    }

    public void Start() => Run(async () =>
    {
        config = await store.LoadAsync(default); Bind();
        await Refresh();
    });

    public void RequestClose()
    {
        if (busy) { closing = true; stop?.Cancel(); return; }
        desktop.Dispose(); Destroy(); Gtk.Application.Quit();
    }

    private async void Run(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        Available();
        try { await action(); }
        catch (OperationCanceledException) { status.Text = T("Canceled"); }
        catch (Exception ex)
        {
            status.Text = T("Failed");
            if (!closing)
            {
                using var dialog = new MessageDialog(this, DialogFlags.Modal, MessageType.Error, ButtonsType.Close, false, "{0}", Error(ex));
                dialog.Run(); dialog.Destroy();
            }
        }
        finally { busy = false; Available(); if (closing) RequestClose(); }
    }

    private void Available()
    {
        bool ready = !busy && config != null;
        network.Sensitive = ready; catalog.Sensitive = ready;
        benchSettings.Sensitive = ready;
        run.Sensitive = ready && Selected != null && config!.Providers.Length > 0;
        cancel.Sensitive = stop != null;
        best.Sensitive = ready && Selected != null && winner != null;
        apply.Sensitive = Selected != null && (Provider == null || Provider.Servers(Family).Length > 0);
        reset.Sensitive = Selected != null;
        var actions = new List<(string Name, System.Action Run)>();
        if (Selected != null && config != null)
        {
            int f = Family;
            foreach (var p in config.Providers.Where(p => p.Servers(f).Length > 0))
                actions.Add((p.Name, () => Run(() => Apply(p, f, p.Servers(f)))));
            actions.Add((T("Reset"), () => Run(Reset)));
        }
        actions.Add((T("Flush"), () => Run(Flush)));
        desktop.MenuFor(this, Selected is { } a ? a.Name + " · IPv" + Family : null, actions, busy);
    }

    private void BuildNetwork()
    {
        family.AppendText("IPv4"); family.AppendText("IPv6"); device.Hexpand = true; provider.Hexpand = true;
        var hero = Parts.Column(0); hero.StyleContext.AddClass("hero");
        var inner = Parts.Inset(hero, 10);
        var identity = Parts.Column(7); identity.Hexpand = true;
        var badgeDot = Parts.Caption("●"); badgeDot.StyleContext.AddClass("connection-dot");
        stateBadge.StyleContext.AddClass("connection-badge"); stateBadge.Halign = Align.Start;
        stateBadge.PackStart(badgeDot, false, false, 0); stateBadge.PackStart(stateText, false, false, 0);
        var context = Parts.Caption(T("Adapter")); context.StyleContext.AddClass("hero-label");
        identity.PackStart(Parts.Row(context, stateBadge), false, false, 0);
        device.StyleContext.AddClass("adapter-choice"); device.Accessible.Name = T("Adapter");
        var refresh = new Button { Image = Parts.Symbol("view-refresh-symbolic"), TooltipText = T("Refresh") };
        refresh.Accessible.Name = T("Refresh"); refresh.StyleContext.AddClass("hero-refresh");
        refresh.Clicked += (_, _) => Run(Refresh);
        identity.PackStart(Parts.Row(device, refresh), false, false, 0);
        var sketch = new Image(Appearance.Picture("route-map.png", 174, 61)) { MarginStart = 25, MarginEnd = 5 };
        inner.PackStart(Parts.Row(identity, sketch), false, false, 0);
        var rule = new Separator(Orientation.Horizontal); rule.StyleContext.AddClass("hero-rule");
        inner.PackStart(rule, false, false, 0);
        Box Metric(string key, Label value)
        {
            var box = Parts.Column(3);
            var title = Parts.Caption(T(key)); title.StyleContext.AddClass("hero-label");
            value.StyleContext.AddClass("hero-value"); value.Selectable = true;
            value.LineWrapMode = Pango.WrapMode.WordChar; value.MaxWidthChars = 62;
            box.PackStart(title, false, false, 0); box.PackStart(value, false, false, 0);
            return box;
        }
        var ip = Metric("IPv4", currentIp); ip.WidthRequest = 152;
        var dnsState = Metric("CurrentDns", currentDns); dnsState.Hexpand = true;
        inner.PackStart(Parts.Row(ip, dnsState), false, false, 0);
        network.PackStart(hero, false, false, 0);

        var card = Parts.Card(); var form = Parts.Inset(card, 12);
        form.PackStart(Parts.Title("NetworkTitle", "section-title"), false, false, 0);
        var providerField = Parts.Field("Provider", provider); providerField.Hexpand = true;
        var protocolField = Parts.Field("Protocol", family); protocolField.WidthRequest = 140;
        form.PackStart(Parts.Row(providerField, protocolField), false, false, 0);
        primary.StyleContext.AddClass("server-entry"); secondary.StyleContext.AddClass("server-entry");
        var p = Parts.Field("Primary", primary); var s = Parts.Field("Secondary", secondary); p.Hexpand = s.Hexpand = true;
        var fields = Parts.Row(p, s); fields.Homogeneous = true;
        form.PackStart(fields, false, false, 0);
        var line = new Separator(Orientation.Horizontal); line.StyleContext.AddClass("action-rule");
        form.PackStart(line, false, false, 0);
        apply = Parts.Button("Apply", () => Run(() => Apply(Provider, Family,
            string.IsNullOrWhiteSpace(secondary.Text) ? [primary.Text] : [primary.Text, secondary.Text])), true);
        apply.Image = Parts.Symbol("object-select-symbolic"); apply.AlwaysShowImage = true;
        reset = Parts.Button("Reset", () => Run(Reset));
        var controls = Parts.Row(apply, reset);
        controls.PackEnd(Parts.Button("Flush", () => Run(Flush)), false, false, 0);
        form.PackStart(controls, false, false, 0); network.PackStart(card, false, false, 0);
        device.Changed += (_, _) =>
        {
            if (binding || Selected == null) return;
            var id = Selected.Id; ClearResults(); Describe();
            Run(() => Save(c => c with { AdapterId = id }));
        };
        provider.Changed += (_, _) => {
            if (binding) return; var id = provider.ActiveId ?? "custom";
            Run(() => Save(c => c with { ProviderId = id, PreferAlternate = false }));
        };
        family.Changed += (_, _) => {
            if (binding) return; int f = Family;
            Run(() => Save(c => c with { Family = f, PreferAlternate = false }));
        };
    }

    private async Task Save(Func<AppConfig, AppConfig> update)
    {
        config = await store.UpdateAsync(update, default); Bind();
    }

    private void Bind()
    {
        if (config == null) return;
        binding = true;
        try
        {
            provider.RemoveAll();
            foreach (var p in config.Providers) provider.Append(p.Id, p.Name);
            provider.Append("custom", T("Custom")); provider.ActiveId = config.ProviderId;
            family.Active = config.Family == 4 ? 0 : 1;
            var values = Provider?.Servers(Family) ?? (Family == 4 ? config.CustomV4 : config.CustomV6);
            if (Provider != null && config.PreferAlternate) values = values.Reverse().ToArray();
            primary.IsEditable = secondary.IsEditable = Provider == null;
            primary.Text = values.ElementAtOrDefault(0) ?? ""; secondary.Text = values.ElementAtOrDefault(1) ?? "";
            domains.Text = string.Join(", ", config.Domains); rounds.Value = config.Rounds;
            FillCatalog();
        }
        finally { binding = false; }
    }

    private async Task Refresh()
    {
        string? wanted = Selected?.Id ?? config?.AdapterId;
        var items = await adapters.ActiveAsync(default);
        binding = true;
        try
        {
            live = items; device.RemoveAll();
            foreach (var a in live) device.AppendText(a.Name + "  ·  " + a.Profile);
            device.Active = wanted == null && live.Length > 0 ? 0 : Array.FindIndex(live, a => a.Id == wanted);
        }
        finally { binding = false; }
        if (wanted != Selected?.Id) ClearResults();
        Describe();
    }

    private void Describe()
    {
        static string Join(string[] list) => list.Length == 0 ? "—" : string.Join(", ", list);
        if (Selected is { } a)
        {
            stateText.Text = T("Connected"); stateBadge.StyleContext.RemoveClass("offline");
            currentIp.Text = Join(a.Ipv4); currentDns.Text = Join(a.Dns);
        }
        else
        {
            stateText.Text = T("Offline"); stateBadge.StyleContext.AddClass("offline");
            currentIp.Text = currentDns.Text = "—"; status.Text = T(live.Length == 0 ? "NoAdapters" : "ChooseAdapter");
        }
        benchDevice.Text = T("Adapter") + " · " + (Selected?.Name ?? "—");
    }

    private async Task Apply(Provider? p, int f, string[] ips)
    {
        var a = Selected ?? throw new AppError("Disconnected");
        ips = Validation.Addresses(ips, f, true);
        await dns.ApplyAsync(a.Id, f, ips, default);
        status.Text = T("Applied") + "  " + a.Name + " · " + string.Join(", ", ips);
        await AfterChange(c => c with { AdapterId = a.Id, Family = f, ProviderId = p?.Id ?? "custom",
            PreferAlternate = p != null && p.Servers(f).Length == 2 && ips[0] == p.Servers(f)[1],
            CustomV4 = p == null && f == 4 ? ips : c.CustomV4, CustomV6 = p == null && f == 6 ? ips : c.CustomV6 });
    }

    private async Task Reset()
    {
        var a = Selected ?? throw new AppError("Disconnected");
        await dns.ResetAsync(a.Id, default); status.Text = T("ResetDone") + "  " + a.Name;
        await AfterChange(c => c with { AdapterId = a.Id });
    }

    private async Task AfterChange(Func<AppConfig, AppConfig> update)
    {
        try { await Save(update); await Refresh(); }
        catch (Exception e) { throw new AppError("PostChange", Error(e), e); }
    }

    private async Task Flush() { await dns.FlushAsync(default); status.Text = T("Flushed"); }
}
