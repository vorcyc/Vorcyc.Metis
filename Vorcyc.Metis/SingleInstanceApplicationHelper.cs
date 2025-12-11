namespace Vorcyc.Metis;


using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;

internal static class SingleInstanceApplicationHelper
{
    /// <summary>
    /// 在应用入口点注册单实例应用
    /// </summary>
    /// <param name="name">应用名</param>
    /// <param name="app">当前WPF应用实例</param>
    public static void Make(string name, System.Windows.Application app)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("只支持 Windows 系统平台");


        string eventName = $"{Environment.MachineName}-{Environment.CurrentDirectory.Replace('\\', '-')}-{name}";
        bool isFirstInstance = false;

        try
        {
            using (EventWaitHandle.OpenExisting(eventName))
            {
                // 不是第一个实例
                isFirstInstance = false;
            }
        }
        catch
        {
            // 是第一个实例
            isFirstInstance = true;
        }

        if (isFirstInstance)
        {
            using (var eventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, eventName))
            {
                ThreadPool.RegisterWaitForSingleObject(eventWaitHandle, WaitOrTimerCallback, app, Timeout.Infinite, false);
            }
        }
        else
        {
            using (var eventWaitHandle = EventWaitHandle.OpenExisting(eventName))
            {
                eventWaitHandle.Set();
            }

            // 退出应用程序
            Environment.Exit(0);
        }
    }


    private static void WaitOrTimerCallback(object state, bool timedOut)
    {
        var app = (System.Windows.Application)state;
        app.Dispatcher.BeginInvoke(new Action(() =>
        {
            app.MainWindow.Activate();
            app.MainWindow.BringIntoView();


            IntPtr hWnd = new WindowInteropHelper(app.MainWindow).Handle;
            // 在任务栏上闪一下
            FlashWindow(hWnd, true);
        }));
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindow(nint hWnd, bool bInvert);

}
