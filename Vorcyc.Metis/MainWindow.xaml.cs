using Microsoft.WindowsAPICodePack.Taskbar;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vorcyc.Metis.Classifiers.Text;
using Vorcyc.Metis.Storage.SQLiteStorage;

namespace Vorcyc.Metis;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{

    private DispatcherTimer? _autoScrollTimer;
    private bool _pauseScroll;

    Vorcyc.PowerLibrary.Windows.Wpf.DragMoveExtender _dragMoveExtender;


    private WindowInteropHelper _windowInterop;
    private List<ThumbnailToolBarButton> thumbnailToolBarButtons = new List<ThumbnailToolBarButton>();


    public MainWindow()
    {
        InitializeComponent();
        _windowInterop = new WindowInteropHelper(this);
        this.Loaded += MainWindow_Loaded;
        this.Unloaded += MainWindow_Unloaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _autoScrollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(50) // scroll cadence
        };
        _autoScrollTimer.Tick += AutoScrollTimer_Tick;
        _autoScrollTimer.Start();


        _dragMoveExtender = new(this.LayoutRoot)
        {
            MouseDownCursor = Cursors.Hand,
            Affinity = 10
        };


        #region 添加系统任务栏按钮支持

        if (TaskbarManager.IsPlatformSupported)
        {
            thumbnailToolBarButtons.Add(new ThumbnailToolBarButton(Properties.Resources.play, "Play"));
            thumbnailToolBarButtons.Add(new ThumbnailToolBarButton(Properties.Resources.pause, "Pause") { Enabled = false });
            thumbnailToolBarButtons.Add(new ThumbnailToolBarButton(Properties.Resources.stop, "Stop"));

            foreach (var ttbb in thumbnailToolBarButtons)
                ttbb.Click += new EventHandler<ThumbnailButtonClickedEventArgs>(ttbb_Click);

            TaskbarManager.Instance.ThumbnailToolBars.AddButtons(_windowInterop.Handle,
                                                                    thumbnailToolBarButtons.ToArray());
        }
        #endregion

        NewsReader.Instance.ArticleChanged += NewsReader_ArticleChanged;
    }

    void ttbb_Click(object sender, ThumbnailButtonClickedEventArgs e)
    {
        switch (e.ThumbnailButton.Tooltip)
        {
            case "Play":
                break;
            case "Pause":
                break;
            case "Stop":
             
                break;
            default:
                break;
        }
    }


    private void MainWindow_Unloaded(object? sender, RoutedEventArgs e)
    {
        if (_autoScrollTimer is not null)
        {
            _autoScrollTimer.Stop();
            _autoScrollTimer.Tick -= AutoScrollTimer_Tick;
            _autoScrollTimer = null;
        }

        NewsReader.Instance.ArticleChanged -= NewsReader_ArticleChanged;
    }

    // Timer tick: auto scroll down; when reaching bottom, wait briefly then reset to top
    private void AutoScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (_pauseScroll || ContentScrollViewer is null)
        {
            return;
        }

        const double step = 0.8; // pixels per tick; tune as needed
        var sv = ContentScrollViewer;

        // If content not exceeding viewport, skip scrolling
        if (sv.ScrollableHeight <= 0)
        {
            return;
        }

        var next = sv.VerticalOffset + step;

        if (next >= sv.ScrollableHeight)
        {
            // small idle at bottom, then reset
            sv.ScrollToVerticalOffset(sv.ScrollableHeight);
            // optional: delay reset by a second
            _autoScrollTimer!.Stop();
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(1000);
                sv.ScrollToTop();
                _autoScrollTimer!.Start();
            }, DispatcherPriority.Background);
        }
        else
        {
            sv.ScrollToVerticalOffset(next);
        }
    }


    private void Image_MouseEnter(object sender, MouseEventArgs e)
    {
        var img = (Image)sender;
        img.Source = new BitmapImage(new Uri("pack://application:,,,/Images/exit_hover.png", UriKind.Absolute));
    }

    private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        //Application.Current.Shutdown();
        this.Close();
    }

    private void Image_MouseLeave(object sender, MouseEventArgs e)
    {
        var img = (Image)sender;
        img.Source = new BitmapImage(new Uri("pack://application:,,,/Images/exit.png", UriKind.Absolute));

    }


    private void btnPrevious_Click(object sender, RoutedEventArgs e)
    {
        NewsReader.Instance.Previous();
    }

    private void btnNext_Click(object sender, RoutedEventArgs e)
    {
        NewsReader.Instance.Next();
    }

    // Pause on hover (optional)
    private void ContentScrollViewer_MouseEnter(object sender, MouseEventArgs e)
    {
        _pauseScroll = true;
    }

    private void ContentScrollViewer_MouseLeave(object sender, MouseEventArgs e)
    {
        _pauseScroll = false;
    }

    // When article changes, update UI and reset scroll to top
    private void NewsReader_ArticleChanged(ArchiveEntity obj)
    {
        CurrentArchive = obj;
        ContentScrollViewer?.ScrollToTop();
    }







    // INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));


    private ArchiveEntity? _currentArchive;


    public ArchiveEntity? CurrentArchive
    {
        get => _currentArchive;
        set
        {
            if (!Equals(_currentArchive, value))
            {
                _currentArchive = value;
                OnPropertyChanged(nameof(CurrentArchive));     // notify bindings to CurrentArchive and its nested paths
                OnPropertyChanged(nameof(ArchiveMetaLine));    // notify derived meta line
            }
        }
    }

    // 组合 发布者 | 类别(友好中文) | 时间(友好本地)
    public string ArchiveMetaLine
    {
        get
        {
            if (CurrentArchive is null)
            {
                return string.Empty;
            }

            var publisher = string.IsNullOrWhiteSpace(CurrentArchive.Publisher) ? "佚名" : CurrentArchive.Publisher!.Trim();
            var category = PageCategoryBuilder.ToFriendlyChinese(CurrentArchive.CategoryValue);
            var time = CurrentArchive.PublishTime is DateTimeOffset dto
                ? NewsReader.ToFriendlyLocalString(dto)
                : "未知时间";

            return $"{publisher} | {category} | {time}";
        }
    }


}
