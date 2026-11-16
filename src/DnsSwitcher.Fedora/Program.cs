using System.Globalization;
using DnsSwitcher.Core.Models;
using DnsSwitcher.Core.Services;
using DnsSwitcher.Fedora.Services;
using DnsSwitcher.Fedora.UI;
using static DnsSwitcher.Fedora.Resources.Strings;

namespace DnsSwitcher.Fedora;

internal static class Program
{
    public static int Main(string[] args)
    {
        var ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt" ? "pt-BR" : "en";
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(ui);
        var runner = new CommandRunner();
        var adapters = new AdapterService(runner);
        var dns = new DnsService(runner, adapters);
        var store = new ConfigService(AppContext.BaseDirectory);
        try
        {
            if (args is ["--install-launcher"])
            {
                Launcher.RegisterAsync().GetAwaiter().GetResult();
                Console.WriteLine(T("LauncherAdded"));
                return 0;
            }
            if (args.Length > 0) return CommandLine.RunAsync(args, store, adapters, dns).GetAwaiter().GetResult();
            GLib.Global.ProgramName = Appearance.AppId;
            if (!Gtk.Application.InitCheck(Appearance.AppId, ref args)) throw new AppError("GuiUnavailable");
            Gdk.Global.ProgramClass = Appearance.AppId;
            GLib.Global.ApplicationName = "DNS Switcher";
            Appearance.Apply();
            SynchronizationContext.SetSynchronizationContext(new GtkContext());
            var window = new MainWindow(store, adapters, dns, new BenchmarkService());
            window.ShowAll();
            GLib.Idle.Add(() => { window.Start(); return false; });
            Gtk.Application.Run();
            GC.KeepAlive(window);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(Error(ex)); return ex is AppError { Key: "Usage" } ? 2 : 1; }
    }

    private sealed class GtkContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            GLib.Idle.Add(() => { d(state); return false; });
    }
}
