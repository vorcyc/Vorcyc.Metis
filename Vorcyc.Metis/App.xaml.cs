using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using Vorcyc.Metis.Services;

namespace Vorcyc.Metis;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{


    private IHost _host;

    protected override async void OnStartup(StartupEventArgs e)
    {

        await NewsReader.Instance.InitAsync();

        if (!Directory.Exists("Chrome"))
            ExtractChrome();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {


                services.AddLogging(builder => builder.AddDebug());

                services.AddHostedService<CrawlingStorageService>();


            })
            .Build();

        await _host.StartAsync();

        base.OnStartup(e);
    }



    protected override void OnExit(ExitEventArgs e)
    {

        NewsReader.Instance.SaveConfigSafe();

        _host?.StopAsync();

        KillChromeProcesses();

        base.OnExit(e);
    }




    static void KillChromeProcesses()
    {

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (!string.Equals(p.ProcessName, "chrome", StringComparison.OrdinalIgnoreCase))
                    continue;

                var desc = GetDescriptionSafe(p);
                if (desc.IndexOf("Google Chrome for Testing", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var titleEmpty = string.IsNullOrEmpty(p.MainWindowTitle);
                var path = GetPathSafe(p);

                // Act on the process
                p.Kill();
            }
            catch
            {
                // Swallow or log as needed
            }
            finally
            {
                p.Dispose();
            }
        }

        static string GetDescriptionSafe(Process p)
        {
            try
            {
                return p.MainModule?.FileVersionInfo?.FileDescription ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        static string GetPathSafe(Process p)
        {
            try
            {
                return p.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    static void ExtractChrome()
    {
        var zipPath = Path.GetFullPath("chrome_archives.zip");

        using var stream = File.OpenRead(zipPath);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: Encoding.Default);

        foreach (var entry in archive.Entries)
        {
            var destinationPath = entry.FullName;
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            if (string.IsNullOrEmpty(entry.Name))
                continue; // directory entry

            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }


}
