using Microsoft.WindowsAPICodePack.Taskbar;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vorcyc.Metis.Classifiers.Text;
using Vorcyc.Metis.Storage.SQLiteStorage;
// WinForms NotifyIcon for system tray
using System.Drawing;
using System.Windows.Forms;
using Image = System.Windows.Controls.Image;
using MessageBox = System.Windows.MessageBox;

namespace Vorcyc.Metis;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    #region Fields

    private DispatcherTimer? _autoScrollTimer;
    private bool _pauseScroll;

    private Vorcyc.PowerLibrary.Windows.Wpf.DragMoveExtender _dragMoveExtender = null!;
    private WindowInteropHelper _windowInterop = null!;
    private readonly List<ThumbnailToolBarButton> _thumbnailButtons = new();

    private NotifyIcon? _tray;
    private ToolStripMenuItem? _autoPlayItem;
    private ToolStripMenuItem? _interestMenu;
    private readonly Dictionary<PageContentCategory, ToolStripMenuItem> _interestItems = new();
    private PageContentCategory _selectedInterests = PageContentCategory.None;

    private ArchiveEntity? _currentArchive;

    #endregion




    #region Constructor & Lifecycle

    public MainWindow()
    {
        InitializeComponent();

        _windowInterop = new WindowInteropHelper(this);
        Loaded += MainWindow_Loaded;
        Unloaded += MainWindow_Unloaded;
    }

    private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        SetupAutoScroll();
        SetupDragMove();
        SetupTaskbarThumbnailButtons();

        // 初始化兴趣选中状态与自动连播状态自 NewsReader
        _selectedInterests = NewsReader.Instance.SelectedCategory;

        SetupSystemTray();

        // Reuse tray menu on window right-click
        MouseRightButtonUp += MainWindow_MouseRightButtonUp;

        NewsReader.Instance.ArticleChanged += NewsReader_ArticleChanged;
    }

    private void MainWindow_Unloaded(object? sender, RoutedEventArgs e)
    {
        TeardownAutoScroll();
        TeardownTaskbarThumbnailButtons();
        TeardownSystemTray();

        // Unwire right-click handler
        MouseRightButtonUp -= MainWindow_MouseRightButtonUp;

        NewsReader.Instance.ArticleChanged -= NewsReader_ArticleChanged;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            Hide(); // minimize to tray
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        TeardownSystemTray();
        base.OnClosed(e);
    }

    #endregion

    #region Setup/Teardown helpers

    private void SetupAutoScroll()
    {
        _autoScrollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _autoScrollTimer.Tick += AutoScrollTimer_Tick;
        _autoScrollTimer.Start();
    }

    private void TeardownAutoScroll()
    {
        if (_autoScrollTimer is not null)
        {
            _autoScrollTimer.Stop();
            _autoScrollTimer.Tick -= AutoScrollTimer_Tick;
            _autoScrollTimer = null;
        }
    }

    private void SetupDragMove()
    {
        _dragMoveExtender = new(this.LayoutRoot)
        {
            MouseDownCursor = System.Windows.Input.Cursors.Hand,
            Affinity = 10
        };
    }

    private void SetupTaskbarThumbnailButtons()
    {
        if (!TaskbarManager.IsPlatformSupported)
        {
            return;
        }

        _thumbnailButtons.Add(new ThumbnailToolBarButton(Properties.Resources.play, "Play"));
        _thumbnailButtons.Add(new ThumbnailToolBarButton(Properties.Resources.pause, "Pause") { Enabled = false });
        _thumbnailButtons.Add(new ThumbnailToolBarButton(Properties.Resources.stop, "Stop"));

        foreach (var btn in _thumbnailButtons)
        {
            btn.Click += ttbb_Click;
        }

        TaskbarManager.Instance.ThumbnailToolBars.AddButtons(_windowInterop.Handle, _thumbnailButtons.ToArray());
    }

    private void TeardownTaskbarThumbnailButtons()
    {
        foreach (var btn in _thumbnailButtons)
        {
            btn.Click -= ttbb_Click;
        }
        _thumbnailButtons.Clear();
    }

    private void SetupSystemTray()
    {
        if (_tray is not null) return;

        _tray = new NotifyIcon
        {
            Text = "Vorcyc Metis",
            Icon = new Icon(SystemIcons.Application, 40, 40),
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };

        // Show
        _tray.ContextMenuStrip.Items.Add("显示主窗口", null, (_, __) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });

        // Separator
        _tray.ContextMenuStrip.Items.Add(new ToolStripSeparator());

        // 自动连播（可勾选），位于“兴趣”之前
        _autoPlayItem = new ToolStripMenuItem("自动连播")
        {
            CheckOnClick = true,
            Checked = NewsReader.Instance.AutoPlay
        };
        _autoPlayItem.CheckedChanged += AutoPlayItem_CheckedChanged;
        _tray.ContextMenuStrip.Items.Add(_autoPlayItem);

        // 兴趣（复选子菜单）
        _interestMenu = new ToolStripMenuItem("兴趣");
        BuildInterestMenu(_interestMenu);
        _tray.ContextMenuStrip.Items.Add(_interestMenu);


        // Separator + About + Exit
        _tray.ContextMenuStrip.Items.Add(new ToolStripSeparator());

        var aboutItem = new ToolStripMenuItem("关于");
        aboutItem.Click += ShowAboutMenu_Click;
        _tray.ContextMenuStrip.Items.Add(aboutItem);


        // Separator + Exit
        _tray.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _tray.ContextMenuStrip.Items.Add("退出", null, (_, __) => App.Current.Shutdown());

        _tray.DoubleClick += (_, __) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };
    }


    private void ShowAboutMenu_Click(object? sender, EventArgs e)
    {
        var text =
            "产品名: Vorcyc Metis 1.0\n" +
            "功能: 从几个网站抓取数据，进行分类并朗读给用户听。\n" +
            "所属单位: 昆明涡旋科技有限公司\n" +
            "作者: cyclone_dll  <cyclone_dll@hotmail.com>\n" +
            "作者: YuanZun Zhang <123@qq.coim>\n"+
            "github: https://github.com/vorcyc/Vorcyc.Metis";
        MessageBox.Show(this, text, "关于", MessageBoxButton.OK, MessageBoxImage.Information);
    }


    private void TeardownSystemTray()
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        _interestItems.Clear();
        _interestMenu = null;
        _autoPlayItem = null;
    }

    #endregion

    #region Context menu reuse

    // Show the tray context menu at the mouse location when right-clicking the window
    private void MainWindow_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_tray?.ContextMenuStrip is null) return;

        var pt = e.GetPosition(this);
        var screen = PointToScreen(pt);
        var winFormsPoint = new System.Drawing.Point((int)screen.X, (int)screen.Y);
        _tray.ContextMenuStrip.Show(winFormsPoint);
    }

    #endregion

    #region AutoPlay toggle

    private void AutoPlayItem_CheckedChanged(object? sender, EventArgs e)
    {
        if (_autoPlayItem is null) return;

        NewsReader.Instance.AutoPlay = _autoPlayItem.Checked;
    }


    private void btnPlayStop_Checked(object sender, RoutedEventArgs e)
    {
        NewsReader.Instance.IsPlaying = true;
    }

    private void btnPlayStop_Unchecked(object sender, RoutedEventArgs e)
    {
        NewsReader.Instance.IsPlaying = false;
    }

    #endregion

    #region Interest submenu

    private void BuildInterestMenu(ToolStripMenuItem root)
    {
        root.DropDownItems.Clear();
        _interestItems.Clear();

        void AddFlag(PageContentCategory flag, string text)
        {
            var item = new ToolStripMenuItem(text)
            {
                CheckOnClick = true,
                Checked = (_selectedInterests & flag) == flag,
                Tag = flag
            };
            item.CheckedChanged += InterestItem_CheckedChanged;
            root.DropDownItems.Add(item);
            _interestItems[flag] = item;
        }

        AddFlag(PageContentCategory.Edu, "教育");
        AddFlag(PageContentCategory.Entertainment, "娱乐");
        AddFlag(PageContentCategory.House, "房产");
        AddFlag(PageContentCategory.Tech, "科技");
        AddFlag(PageContentCategory.Sports, "体育");
        AddFlag(PageContentCategory.Car, "汽车");
        AddFlag(PageContentCategory.Culture, "文化");
        AddFlag(PageContentCategory.Game, "游戏");
        AddFlag(PageContentCategory.Travel, "旅游");
        AddFlag(PageContentCategory.Military, "军事");
        AddFlag(PageContentCategory.World, "国际");
        AddFlag(PageContentCategory.Finance, "财经");
        AddFlag(PageContentCategory.Agriculture, "农业");
        AddFlag(PageContentCategory.Story, "故事");
        AddFlag(PageContentCategory.Stock, "股票");
        AddFlag(PageContentCategory.DomesticPolitics, "国内政治");

        root.DropDownItems.Add(new ToolStripSeparator());

        AddFlag(PageContentCategory.Politics, "国际政治（英文）");
        AddFlag(PageContentCategory.Sport, "体育（英文）");
        AddFlag(PageContentCategory.Business, "商业（英文）");

        root.DropDownItems.Add(new ToolStripSeparator());

        var selectAll = new ToolStripMenuItem("全选");
        selectAll.Click += (_, __) => SetAllInterests(true);
        root.DropDownItems.Add(selectAll);

        var clearAll = new ToolStripMenuItem("清空");
        clearAll.Click += (_, __) => SetAllInterests(false);
        root.DropDownItems.Add(clearAll);
    }

    private void InterestItem_CheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem item || item.Tag is not PageContentCategory flag)
            return;

        if (item.Checked)
        {
            _selectedInterests |= flag;
        }
        else
        {
            _selectedInterests &= ~flag;
        }

        // 同步到 NewsReader.Instance.SelectedCategory
        NewsReader.Instance.SelectedCategory = _selectedInterests;

        OnPropertyChanged(nameof(ArchiveMetaLine));
    }

    private void SetAllInterests(bool check)
    {
        foreach (var kvp in _interestItems)
        {
            kvp.Value.Checked = check;
        }

        _selectedInterests = check ? PageContentCategory.All : PageContentCategory.None;

        // 同步到 NewsReader.Instance.SelectedCategory
        NewsReader.Instance.SelectedCategory = _selectedInterests;

        OnPropertyChanged(nameof(ArchiveMetaLine));
    }

    #endregion

    #region Taskbar Thumbnail Buttons

    private void ttbb_Click(object? sender, ThumbnailButtonClickedEventArgs e)
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

    #endregion

    #region Auto-scroll

    private void AutoScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (_pauseScroll || ContentScrollViewer is null)
        {
            return;
        }

        const double step = 0.8; // pixels per tick
        var sv = ContentScrollViewer;

        if (sv.ScrollableHeight <= 0)
        {
            return;
        }

        var next = sv.VerticalOffset + step;

        if (next >= sv.ScrollableHeight)
        {
            sv.ScrollToVerticalOffset(sv.ScrollableHeight);

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

    private void ContentScrollViewer_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _pauseScroll = true;
    }

    private void ContentScrollViewer_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _pauseScroll = false;
    }

    #endregion

    #region UI events

    private void Image_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var img = (Image)sender;
        img.Source = new BitmapImage(new Uri("pack://application:,,,/Images/exit_hover.png", UriKind.Absolute));
    }

    private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Close();
    }

    private void Image_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
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

    #endregion

    #region NewsReader bindings

    private void NewsReader_ArticleChanged(ArchiveEntity obj)
    {
        CurrentArchive = obj;
        ContentScrollViewer?.ScrollToTop();
    }

    public ArchiveEntity? CurrentArchive
    {
        get => _currentArchive;
        set
        {
            if (!Equals(_currentArchive, value))
            {
                _currentArchive = value;
                OnPropertyChanged(nameof(CurrentArchive));
                OnPropertyChanged(nameof(ArchiveMetaLine));
            }
        }
    }

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

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    #endregion
}