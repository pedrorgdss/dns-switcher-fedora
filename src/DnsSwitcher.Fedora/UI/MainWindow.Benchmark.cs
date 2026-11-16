using DnsSwitcher.Core.Models;
using DnsSwitcher.Core.Services;
using Gtk;
using static DnsSwitcher.Fedora.Resources.Strings;

namespace DnsSwitcher.Fedora.UI;

internal sealed partial class MainWindow
{
    private readonly Box benchSettings = Parts.Column(8);
    private readonly Label benchDevice = Parts.Caption("");
    private readonly Entry domains = new();
    private readonly SpinButton rounds = new(1, 20, 1);
    private readonly ProgressBar progress = new();
    private Button run = null!, cancel = null!, best = null!;
    private readonly Box noResults = Parts.Column(8);
    private ListStore results = null!;
    private BenchmarkResult? winner;

    private void BuildBenchmark()
    {
        var title = Parts.Row(Parts.Title("BenchmarkTitle"));
        benchDevice.StyleContext.AddClass("field-name"); title.PackEnd(benchDevice, false, false, 0);
        bench.PackStart(title, false, false, 0);
        var card = Parts.Card(); var body = Parts.Inset(card, 12);
        bench.PackStart(card, false, false, 0);
        var names = Parts.Field("Domains", domains); names.Hexpand = true;
        benchSettings.PackStart(Parts.Row(names, Parts.Field("Rounds", rounds)), false, false, 0);
        body.PackStart(benchSettings, false, false, 0);
        run = Parts.Button("Run", () => Run(TestSpeed), true);
        cancel = Parts.Button("Cancel", () => stop?.Cancel());
        best = Parts.Button("Fastest", () => Run(async () =>
        {
            if (winner is not { } r) return;
            int f = r.Server.Contains(':') ? 6 : 4;
            await Apply(r.Provider, f, r.Provider.Servers(f).OrderByDescending(ip => ip == r.Server).ToArray());
        }));
        var actions = Parts.Row(run, cancel); actions.PackEnd(best, false, false, 0);
        body.PackStart(actions, false, false, 0);
        bench.PackStart(progress, false, false, 0);
        var table = Parts.Table(["Provider","Server","Mean","Min","Max","Failures"], out results, out _);
        var overlay = new Overlay(); overlay.Add(table);
        noResults.Halign = noResults.Valign = Align.Center;
        noResults.PackStart(Parts.Symbol("power-profile-performance-symbolic", 28), false, false, 0);
        noResults.PackStart(Parts.Caption(T("BenchmarkEmpty")), false, false, 0);
        noResults.StyleContext.AddClass("dim-label"); overlay.AddOverlay(noResults);
        overlay.SetOverlayPassThrough(noResults, true); bench.PackStart(overlay, true, true, 0);
    }

    private sealed class Counter : IProgress<int>
    {
        private int value;
        public int Value => Volatile.Read(ref value);
        public void Report(int _) => Interlocked.Increment(ref value);
    }
    private void ClearResults() { results.Clear(); noResults.Show(); winner = null; progress.Fraction = 0; }

    private async Task TestSpeed()
    {
        var a = Selected ?? throw new AppError("Disconnected");
        var d = Validation.Domains(domains.Text.Split([',',';',' ','\t','\n'], StringSplitOptions.RemoveEmptyEntries));
        var n = rounds.ValueAsInt;
        await Save(c => c with { Domains = d, Rounds = n });
        a = (await adapters.ActiveAsync(default)).FirstOrDefault(current => current.Name == a.Name && current.Id == a.Id)
            ?? throw new AppError("Disconnected");
        if (closing) return;
        ClearResults(); noResults.Hide(); var snapshot = config!;
        using var cts = new CancellationTokenSource(); stop = cts; cancel.Sensitive = true;
        var counter = new Counter(); int total = BenchmarkService.QueryCount(snapshot);
        void Update()
        {
            progress.Fraction = (double)counter.Value / Math.Max(1, total);
            status.Text = string.Format(T("Progress"), counter.Value, total);
        }
        Update();
        uint timer = GLib.Timeout.Add(150, () => { Update(); return true; });
        try
        {
            var rows = await benchmark.RunAsync(snapshot, a, counter, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            winner = rows.FirstOrDefault(row => row.Mean.HasValue); Update();
            static string Ms(double? ms) => ms?.ToString("N1") ?? "—";
            foreach (var r in rows) results.AppendValues((r == winner ? "★ " : "") + r.Provider.Name,
                r.Server, Ms(r.Mean), Ms(r.Minimum), Ms(r.Maximum), r.FailureRate.ToString("P0"));
            status.Text = winner == null ? T("NoResponses") :
                string.Format(T("Winner"), winner.Provider.Name, winner.Server, Ms(winner.Mean));
        }
        finally { GLib.Source.Remove(timer); stop = null; noResults.Visible = !results.GetIterFirst(out _); }
    }
}
