using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CopyPastePro.Models;
using CopyPastePro.Services;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;

namespace CopyPastePro;

public partial class QuickAccessWindow : Window
{
  private readonly ClipboardHistoryRepository _repository;
  private readonly ClipboardCaptureService _capture;
  private readonly ClipboardPasteService _paste;
  private readonly AppSettings _settings;
  private readonly Action _openMainWindow;
  private readonly ClipboardQuickActionsService _quickActions;
  private bool _isReady;
  private int _deactivateGraceUntil;
  private bool _suppressDeactivateHide;
  private DispatcherTimer? _deactivateHideTimer;
  private const int DeactivateGraceMs = 1200;

  public QuickAccessWindow(
      ClipboardHistoryRepository repository,
      ClipboardCaptureService capture,
      ClipboardPasteService paste,
      AppSettings settings,
      Action openMainWindow,
      ClipboardQuickActionsService quickActions)
  {
    _repository = repository;
    _capture = capture;
    _paste = paste;
    _settings = settings;
    _openMainWindow = openMainWindow;
    _quickActions = quickActions;
    InitializeComponent();
    ApplySizeFromSettings();
    _isReady = true;
    QuickFooter.Text = "Click item to paste · ★/📌 · Esc close";
    QuickSearch.TextChanged += (_, _) => Refresh();
    Activated += (_, _) => ExtendDeactivateGrace();
  }

  public void ApplySizeFromSettings()
  {
    Width = _settings.QuickAccessWidth;
    Height = _settings.QuickAccessHeight;
    Topmost = _settings.QuickAccessAlwaysOnTop;
  }

  public void ShowNearCursor()
  {
    _suppressDeactivateHide = true;
    ExtendDeactivateGrace();

    PositionWindow();
    Refresh();

    if (Visibility != Visibility.Visible)
      Show();

    Topmost = _settings.QuickAccessAlwaysOnTop;
    WindowFocusHelper.ForceAboveAll(this);

    Dispatcher.BeginInvoke(() =>
    {
      WindowFocusHelper.ForceAboveAll(this);
      QuickSearch.Focus();
      Keyboard.Focus(QuickSearch);
      if (QuickList.Items.Count > 0) QuickList.SelectedIndex = 0;
      ExtendDeactivateGrace();
      _suppressDeactivateHide = false;
    }, DispatcherPriority.Input);
  }

  public void ToggleNearCursor()
  {
    if (IsVisible && IsActive) { Hide(); return; }
    ShowNearCursor();
  }

  private void ExtendDeactivateGrace() =>
      _deactivateGraceUntil = Environment.TickCount + DeactivateGraceMs;

  public void Refresh()
  {
    if (!_isReady) return;
    var items = _repository.Query(new HistoryQuery
    {
      Search = QuickSearch.Text.Trim(),
      Sort = _settings.DefaultSort,
      Take = _settings.QuickAccessItemCount
    }).Select(e => new QuickItem(e)).ToList();
    QuickList.ItemsSource = items;
    QuickSubtitle.Text = $"{items.Count} items · {_settings.QuickAccessModifiers}+{_settings.QuickAccessKey}";
  }

  private void PositionWindow()
  {
    if (_settings.QuickAccessPosition == "CenterScreen")
    {
      WindowStartupLocation = WindowStartupLocation.CenterScreen;
      return;
    }

    WindowStartupLocation = WindowStartupLocation.Manual;
    var pt = GetCursorPosition();
    var src = PresentationSource.FromVisual(this);
    var scale = src?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
    Left = pt.X / scale + 12;
    Top = pt.Y / scale + 12;

    if (Left + Width > SystemParameters.VirtualScreenWidth)
      Left = SystemParameters.VirtualScreenWidth - Width - 16;
    if (Top + Height > SystemParameters.VirtualScreenHeight)
      Top = SystemParameters.VirtualScreenHeight - Height - 16;
    if (Left < 0) Left = 16;
    if (Top < 0) Top = 16;
  }

  private static System.Drawing.Point GetCursorPosition()
  {
    GetCursorPosNative(out var p);
    return p;
  }

  [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
  private static extern bool GetCursorPosNative(out System.Drawing.Point lpPoint);

  private void PasteEntry(ClipboardEntry entry)
  {
    _repository.BumpToRecent(entry.Id);
    entry.CapturedAt = DateTime.Now;
    using (_capture.SuppressCapture()) _paste.SetClipboard(entry);
    Hide();
    var delay = Math.Max(_settings.PasteDelayMs, 50);
    System.Threading.Tasks.Task.Delay(delay).ContinueWith(_ =>
        Dispatcher.Invoke(ClipboardPasteService.SendPasteKeys));
  }

  private void QuickList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
  {
    if (FindParent<Button>(e.OriginalSource as DependencyObject) != null) return;

    var item = FindParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as QuickItem;
    if (item == null) return;

    QuickList.SelectedItem = item;
    PasteEntry(item.Entry);
    e.Handled = true;
  }

  private void QuickList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) =>
      ClipboardContextMenuBuilder.PrepareListBoxRightClick(QuickList, e);

  private void QuickList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
  {
    if (QuickList.SelectedItem is not QuickItem item)
    {
      QuickList.ContextMenu = BuildQuickActionsOnlyMenu();
      return;
    }

    var entry = item.Entry;
    QuickList.ContextMenu = ClipboardContextMenuBuilder.Create(menu =>
    {
        ClipboardContextMenuBuilder.AddClipboardEntryMenu(menu, new ClipboardEntryMenuOptions
        {
          IsFavorite = entry.IsFavorite,
          IsPinned = entry.IsPinned,
          IsImage = entry.ContentType == ClipboardContentType.Image,
          HasImageFile = false,
          ShowOpenManager = true,
          Actions = new ClipboardEntryMenuActions
          {
            PasteToApp = () => PasteEntry(entry),
            Copy = () =>
            {
              using (_capture.SuppressCapture()) _paste.SetClipboard(entry);
            },
            ToggleFavorite = () =>
            {
              var fav = !entry.IsFavorite;
              _repository.SetFavorite(entry.Id, fav);
              entry.IsFavorite = fav;
              item.UpdateBrushes();
              Refresh();
            },
            TogglePin = () =>
            {
              var pin = !entry.IsPinned;
              _repository.SetPinned(entry.Id, pin);
              entry.IsPinned = pin;
              item.UpdateBrushes();
              Refresh();
            },
            RemoveFromClipboard = () =>
            {
              _repository.Delete(entry.Id);
              Refresh();
            },
            OpenManager = () => _openMainWindow()
          }
        });
      ClipboardContextMenuBuilder.AddQuickClipboardActions(menu, CreateQuickActionsMenuOptions());
    });
  }

  private void QuickFooter_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
  {
    var menu = BuildQuickActionsOnlyMenu();
    menu.PlacementTarget = sender as UIElement ?? this;
    menu.IsOpen = true;
    e.Handled = true;
  }

  private ContextMenu BuildQuickActionsOnlyMenu() =>
      ClipboardContextMenuBuilder.Create(menu =>
          ClipboardContextMenuBuilder.AddQuickClipboardActionsOnly(menu, CreateQuickActionsMenuOptions(includeOpenManager: true)));

  private ClipboardQuickActionsMenuOptions CreateQuickActionsMenuOptions(bool includeOpenManager = false) =>
      new()
      {
        ClearSystemClipboard = () => _quickActions.ClearSystemClipboard(this),
        CopyLatest = () => _quickActions.CopyLatestToClipboard(this),
        PasteLatest = () => _quickActions.PasteLatestToApp(this),
        ClearUnpinned = () => _quickActions.ClearUnpinnedHistory(this),
        ClearAppHistory = () => _quickActions.ClearAppHistory(this),
        ClearAll = () => _quickActions.ClearAllHistory(this),
        ToggleCapture = () => _quickActions.ToggleCapturePaused(this),
        IsCapturePaused = _quickActions.IsCapturePaused,
        OpenManager = includeOpenManager ? () => { Hide(); _openMainWindow(); } : null,
        Refresh = Refresh
      };

  private void QuickList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
  {
    if (sender is not ListBox list) return;
    var scroll = FindVisualChild<ScrollViewer>(list);
    if (scroll == null) return;
    scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta / 3.0);
    e.Handled = true;
  }

  private void QuickSearch_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
  {
    if (e.Key == Key.Down && QuickList.Items.Count > 0)
    {
      QuickList.Focus();
      QuickList.SelectedIndex = 0;
      e.Handled = true;
    }
  }

  private void Favorite_Click(object sender, RoutedEventArgs e)
  {
    e.Handled = true;
    if (sender is not Button btn || btn.Tag is not QuickItem item) return;
    var fav = !item.Entry.IsFavorite;
    _repository.SetFavorite(item.Entry.Id, fav);
    item.Entry.IsFavorite = fav;
    item.UpdateBrushes();
    Refresh();
  }

  private void Pin_Click(object sender, RoutedEventArgs e)
  {
    e.Handled = true;
    if (sender is not Button btn || btn.Tag is not QuickItem item) return;
    var pin = !item.Entry.IsPinned;
    _repository.SetPinned(item.Entry.Id, pin);
    item.Entry.IsPinned = pin;
    item.UpdateBrushes();
    Refresh();
  }

  private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
  {
    if (e.Key == Key.Enter && QuickList.SelectedItem is QuickItem qi)
    {
      PasteEntry(qi.Entry);
      e.Handled = true;
    }
    if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
    if (e.Key == Key.Up && QuickList.SelectedIndex > 0) { QuickList.SelectedIndex--; e.Handled = true; }
    if (e.Key == Key.Down && QuickList.SelectedIndex < QuickList.Items.Count - 1) { QuickList.SelectedIndex++; e.Handled = true; }
  }

  private void Window_Deactivated(object sender, EventArgs e)
  {
    if (!_settings.QuickAccessCloseOnFocusLoss) return;
    if (_suppressDeactivateHide) return;
    if (Environment.TickCount < _deactivateGraceUntil) return;

    _deactivateHideTimer?.Stop();
    _deactivateHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
    _deactivateHideTimer.Tick += OnDeferredDeactivateHide;
    _deactivateHideTimer.Start();
  }

  private void OnDeferredDeactivateHide(object? sender, EventArgs e)
  {
    _deactivateHideTimer?.Stop();
    if (!IsVisible) return;
    if (_suppressDeactivateHide) return;
    if (Environment.TickCount < _deactivateGraceUntil) return;
    if (IsActive) return;

    var self = new WindowInteropHelper(this).Handle;
    var fg = WindowFocusHelper.GetForegroundHwnd();

    if (fg == self) return;

    if (WindowFocusHelper.IsIncidentalOwnWindow(fg, self))
    {
      WindowFocusHelper.ForceAboveAll(this);
      ExtendDeactivateGrace();
      return;
    }

    Hide();
  }

  private void Header_MouseDown(object sender, MouseButtonEventArgs e)
  {
    if (e.ChangedButton == MouseButton.Left) DragMove();
  }

  private void OpenManager_Click(object sender, RoutedEventArgs e)
  {
    Hide();
    _openMainWindow();
  }

  private void Close_Click(object sender, RoutedEventArgs e) => Hide();

  private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
  {
    while (child != null)
    {
      if (child is T found) return found;
      child = VisualTreeHelper.GetParent(child);
    }
    return null;
  }

  private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
  {
    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
    {
      var child = VisualTreeHelper.GetChild(parent, i);
      if (child is T match) return match;
      var nested = FindVisualChild<T>(child);
      if (nested != null) return nested;
    }
    return null;
  }

  public sealed class QuickItem
  {
    public ClipboardEntry Entry { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public System.Windows.Media.Brush FavoriteBrush { get; private set; } = System.Windows.Media.Brushes.Transparent;
    public System.Windows.Media.Brush PinBrush { get; private set; } = System.Windows.Media.Brushes.Transparent;

    public QuickItem(ClipboardEntry e)
    {
      Entry = e;
      Title = e.Preview;
      Subtitle = $"{e.CategoryIcon} {e.Category} · {e.TypeLabel} · {e.TimeAgo}";
      UpdateBrushes();
    }

    public void UpdateBrushes()
    {
      FavoriteBrush = Entry.IsFavorite
          ? (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("AccentLightBrush")
          : (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("TextMutedBrush");
      PinBrush = Entry.IsPinned
          ? (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("AccentLightBrush")
          : (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("TextMutedBrush");
    }
  }
}
