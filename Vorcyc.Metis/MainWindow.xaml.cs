using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Vorcyc.Metis;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{


    Vorcyc.PowerLibrary.Windows.Wpf.DragMoveExtender _dragMoveExtender;

    public MainWindow()
    {
        InitializeComponent();
        this.Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Dwm.ExtendGlassFrame(this, new Thickness(-1));

        _dragMoveExtender = new(this.LayoutRoot)
        {
            MouseDownCursor = Cursors.Hand,
            Affinity = 10
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        Dwm.ExtendGlassFrame(this, new Thickness(-1));
        base.OnSourceInitialized(e);
    }


    private void LayoutRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        //this.DragMove();
    }

    private void Image_MouseEnter(object sender, MouseEventArgs e)
    {
        var img = (Image)sender;
        img.Source = new BitmapImage(new Uri("pack://application:,,,/Images/exit_hover.png", UriKind.Absolute));
    }

    private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void Image_MouseLeave(object sender, MouseEventArgs e)
    {
        var img = (Image)sender;
        img.Source = new BitmapImage(new Uri("pack://application:,,,/Images/exit.png", UriKind.Absolute));

    }
}


internal sealed class Dwm
{
    [DllImport("dwmapi.dll", PreserveSig = false)]
    public static extern void DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);
    [DllImport("dwmapi.dll", PreserveSig = false)]
    public static extern bool DwmIsCompositionEnabled();
    public static bool ExtendGlassFrame(Window window, Thickness margin)
    {
        if (!DwmIsCompositionEnabled())
        {
            return false;
        }
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("The Window must be shown before extending glass.");
        }
        window.Background = Brushes.Transparent;
        HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
        MARGINS margins = new MARGINS(margin);
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
        public MARGINS(Thickness t)
        {
            this = new Dwm.MARGINS();
            this.Left = (int)Math.Round(Math.Round(t.Left));
            this.Right = (int)Math.Round(Math.Round(t.Right));
            this.Top = (int)Math.Round(Math.Round(t.Top));
            this.Bottom = (int)Math.Round(Math.Round(t.Bottom));
        }
    }
}