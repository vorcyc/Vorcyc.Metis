using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using Vorcyc.Metis.CrawlerPrimitives.Services;

namespace Vorcyc.Metis;

/// <summary>
/// 应用程序入口类：负责初始化宿主服务、Chrome 浏览器环境、以及应用生命周期管理。
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>
    /// 后台宿主实例，承载定时爬取等后台服务。
    /// </summary>
    private IHost _host;


    /// <summary>
    /// 应用启动时执行：加载设置、初始化新闻阅读器、解压 Chrome、启动后台宿主服务并创建主窗口。
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        this.ShutdownMode = ShutdownMode.OnMainWindowClose;

        SingleInstanceApplicationHelper.Make("VORCYC METIS", this);


        await ApplicationSettings.Instance.LoadAsync();
        await NewsReader.Instance.InitAsync();


        ////确保浏览器可用（首次会下载 Chromium）
        //var fetcher = new BrowserFetcher();
        //await fetcher.DownloadAsync();
        //自动解压替代
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

        this.MainWindow = new MainWindow();
        this.MainWindow.Show();

    }



    /// <summary>
    /// 应用退出时执行：保存配置、停止后台宿主、清理 Chrome 进程。
    /// </summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        NewsReader.Instance.SaveConfigSafe();
        await ApplicationSettings.Instance.SaveAsync();

        // 等待后台宿主优雅停止，避免火忘导致资源泄漏
        if (_host is not null)
            await _host.StopAsync();

        KillChromeProcesses();

        base.OnExit(e);
    }




    /// <summary>
    /// 清理残留的 Google Chrome for Testing 进程，避免退出后遗留僵尸进程。
    /// </summary>
    static void KillChromeProcesses()
    {
        foreach (var p in Process.GetProcessesByName("chrome"))
        {
            try
            {
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

        /// <summary>安全获取进程的文件描述信息。</summary>
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

        /// <summary>安全获取进程的可执行文件路径。</summary>
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

    /// <summary>
    /// 从 chrome_archives.zip 解压 Chrome 浏览器到当前目录。
    /// 仅在 Chrome 目录不存在时由 <see cref="OnStartup"/> 调用。
    /// </summary>
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
