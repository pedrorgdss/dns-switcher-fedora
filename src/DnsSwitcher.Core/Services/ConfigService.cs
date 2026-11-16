using System.Text.Json;
using System.Text.Json.Serialization;
using DnsSwitcher.Core.Models;

namespace DnsSwitcher.Core.Services;

public sealed class ConfigService(string directory) : IConfigService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };
    private string ConfigPath => Path.Combine(directory, "dnsswitcher.json");
    private const int FileLimit = 256 * 1024;

    public static AppConfig Defaults()
    {
        using var file = typeof(ConfigService).Assembly.GetManifestResourceStream(
            "DnsSwitcher.Core.Resources.providers.default.json")!;
        return Validation.Config(new AppConfig { Providers = JsonSerializer.Deserialize<Provider[]>(file, Json)! });
    }

    public static AppConfig Parse(string json)
    {
        try { return Validation.Config(JsonSerializer.Deserialize<AppConfig>(json, Json)); }
        catch (JsonException ex) { throw new AppError("InvalidJson", ex.Message, ex); }
    }

    public Task<AppConfig> LoadAsync(CancellationToken token) => AccessAsync(null, token);
    public Task<AppConfig> UpdateAsync(Func<AppConfig, AppConfig> change, CancellationToken token) =>
        AccessAsync(change, token);

    private async Task<AppConfig> AccessAsync(Func<AppConfig, AppConfig>? change, CancellationToken token)
    {
        // A persistent lock inode serializes read/modify/write across the GUI and CLI.
        using var gate = await LockAsync(token).ConfigureAwait(false);
        AppConfig config;
        bool created = false;
        try { config = Parse(await ReadAsync(ConfigPath, token).ConfigureAwait(false)); }
        catch (FileNotFoundException) { config = Defaults(); created = true; }
        if (change != null) config = Validation.Config(change(config));
        if (created || change != null)
            await WriteAsync(ConfigPath, config, token).ConfigureAwait(false);
        return config;
    }

    private async Task<FileStream> LockAsync(CancellationToken token)
    {
        for (int attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(ConfigPath + ".lock", FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
            }
            catch (IOException) when (attempt < 40)
            {
                await Task.Delay(50, token).ConfigureAwait(false);
            }
        }
    }

    private static async Task<string> ReadAsync(string path, CancellationToken token)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous);
        if (stream.Length > FileLimit) throw new AppError("FileSize");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(token).ConfigureAwait(false);
    }

    public async Task<Provider[]> ImportAsync(string path, CancellationToken token)
    {
        string json = await ReadAsync(path, token).ConfigureAwait(false);
        try { return Validation.Providers(JsonSerializer.Deserialize<Provider[]>(json, Json)); }
        catch (JsonException ex) { throw new AppError("InvalidJson", ex.Message, ex); }
    }

    public Task ExportAsync(string path, Provider[] providers, CancellationToken token)
    {
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(ConfigPath), StringComparison.Ordinal))
            throw new AppError("ExportConfig");
        return WriteAsync(path, Validation.Providers(providers), token);
    }

    private static async Task WriteAsync<T>(string path, T value, CancellationToken token)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(file, value, Json, token).ConfigureAwait(false);
                await file.FlushAsync(token).ConfigureAwait(false);
            }
            // Same directory/volume: a reader sees either the previous or the complete new file.
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }
}
