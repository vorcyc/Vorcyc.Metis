using System.IO;
using System.Text.Json;

namespace Vorcyc.Metis;

public sealed class ApplicationSettings
{


    private const string file = "settings.json";

    public bool CloseToTray { get; set; } = true;

    public async Task LoadAsync()
    {
        await using var fs = File.Open(GetSettingsPath(), FileMode.Open, FileAccess.Read, FileShare.Read);
        var incoming = await JsonSerializer.DeserializeAsync<ApplicationSettings>(fs, SerializerOptions);
        if (incoming is null)
        {
            return;
        }

        CloseToTray = incoming.CloseToTray;
    }

    public async Task SaveAsync()
    {
        using var fs = File.Open(GetSettingsPath(), FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync<ApplicationSettings>(fs, this, SerializerOptions);
    }

    private static string GetSettingsPath()
    {
        return file;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };






    private static ApplicationSettings? s_instance = null;

    /// <summary>
    /// 单例实例。
    /// </summary>
    public static ApplicationSettings Instance => s_instance ??= new();
}