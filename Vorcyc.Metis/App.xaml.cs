using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using Vorcyc.Metis.Services;

namespace Vorcyc.Metis;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{


    private IHost _host;



    protected override void OnStartup(StartupEventArgs e)
    {


        //ExtractChrome();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {


                services.AddLogging(builder => builder.AddDebug());
                //services.AddHostedService<CrawlingService>();
                services.AddHostedService<CrawlingStorageService>();


            })
            .Build();

        _host.StartAsync();

        base.OnStartup(e);
    }



    protected override void OnExit(ExitEventArgs e)
    {
        _host.StopAsync();

        base.OnExit(e);
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
