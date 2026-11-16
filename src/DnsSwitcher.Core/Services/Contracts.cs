using DnsSwitcher.Core.Models;

namespace DnsSwitcher.Core.Services;

public interface IAdapterService
{
    Task<AdapterInfo[]> ActiveAsync(CancellationToken token);
}

public interface IDnsService
{
    Task ApplyAsync(string adapterId, int family, string[] addresses, CancellationToken token);
    Task ResetAsync(string adapterId, CancellationToken token);
    Task FlushAsync(CancellationToken token);
}

public interface IConfigService
{
    Task<AppConfig> LoadAsync(CancellationToken token);
    Task<AppConfig> UpdateAsync(Func<AppConfig, AppConfig> change, CancellationToken token);
    Task<Provider[]> ImportAsync(string path, CancellationToken token);
    Task ExportAsync(string path, Provider[] providers, CancellationToken token);
}

public interface IBenchmarkService
{
    Task<BenchmarkResult[]> RunAsync(AppConfig config, AdapterInfo adapter, IProgress<int>? completed, CancellationToken token);
}
