using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Engine3D.IO;

public sealed class ClientSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly string _path;
    private readonly IClientLog _log;
    private readonly Channel<ClientSettings> _pending = Channel.CreateBounded<ClientSettings>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest
    });
    private readonly Task _writer;
    private string _lastError = string.Empty;
    private bool _readOnly;

    public ClientSettingsStore(string path, IClientLog log)
    {
        _path = path;
        _log = log;
        _writer = Task.Run(WriteLoopAsync);
    }

    // Beside the executable: the game keeps its settings with itself, never in a per-user profile folder.
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "client-settings.json");

    public string LastError => Volatile.Read(ref _lastError);

    public ClientSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                var defaults = new ClientSettings();
                QueueSave(defaults);
                return defaults;
            }

            var settings = JsonSerializer.Deserialize<ClientSettings>(File.ReadAllBytes(_path), JsonOptions)
                ?? throw new JsonException("Settings document is null.");
            if (settings.Version != ClientSettings.CurrentVersion)
            {
                _readOnly = true;
                ReportError($"Unsupported settings version {settings.Version}; preserving {_path} and using defaults.");
                return new ClientSettings();
            }

            return settings.Validate(_log);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A malformed/unreadable document is retained for recovery. Explicit user changes must not destroy it.
            _readOnly = true;
            ReportError($"Cannot load {_path}; using defaults without overwriting the file: {exception.Message}");
            return new ClientSettings();
        }
    }

    public bool QueueSave(ClientSettings settings)
    {
        return !_readOnly && _pending.Writer.TryWrite(settings);
    }

    public Task CompleteAsync()
    {
        _pending.Writer.TryComplete();
        return _writer;
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var settings in _pending.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
                Directory.CreateDirectory(directory);
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                {
                    await JsonSerializer.SerializeAsync(stream, settings, JsonOptions).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }

                File.Move(temporary, _path, overwrite: true);
                Volatile.Write(ref _lastError, string.Empty);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ReportError($"Cannot save {_path}: {exception.Message}");
            }
            finally
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _log.Warn($"[Settings] Cannot remove temporary file {temporary}: {exception.Message}");
                }
            }
        }
    }

    private void ReportError(string message)
    {
        Volatile.Write(ref _lastError, message);
        _log.Error($"[Settings] {message}");
    }
}
