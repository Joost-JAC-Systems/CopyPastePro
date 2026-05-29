using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CopyPastePro.Models;
using CopyPastePro.Services;
using Forms = System.Windows.Forms;
using ListBox = System.Windows.Controls.ListBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace CopyPastePro.Views;

public partial class ImageLibraryView : System.Windows.Controls.UserControl
{
  private readonly AppSettings _settings;
  private readonly ClipboardHistoryRepository _repository;
  private readonly ImageLibraryService _library;
  private readonly SyncedFolderService _sync;
  private readonly ClipboardPasteService _paste;
  private readonly ClipboardCaptureService _capture;
  private readonly Action<ClipboardEntry>? _pasteToApp;
  private readonly Action? _captureScreenshot;
  private ImageThumbItem? _selected;
  private List<ImageThumbItem> _items = new();
  private bool _focusPreviewMode;
  private bool _syncingListSelection;
  private DispatcherTimer? _chromeHideTimer;
  private static readonly TimeSpan ChromeHideDelay = TimeSpan.FromSeconds(2.5);
  private static readonly Duration ChromeFadeOutDuration = TimeSpan.FromSeconds(0.5);

  public event EventHandler? LibraryChanged;

  public string DefaultAutoSaveFolder => _library.DefaultAutoSaveFolder;

  private Window? DialogOwner => Window.GetWindow(this);

  /// <summary>Reload toggles and library after settings were saved in the manager.</summary>
  public void ApplySettingsFromSaved()
  {
    ApplyLayoutMode();
    UpdateFolderUi();
    Refresh();
  }

  public void ResetPreviewZoom() => DetailPreview.ResetZoom();

  public ImageLibraryView(
      AppSettings settings,
      ClipboardHistoryRepository repository,
      ImageLibraryService library,
      ClipboardPasteService paste,
      ClipboardCaptureService capture,
      Action<ClipboardEntry>? pasteToApp = null,
      Action? captureScreenshot = null)
  {
    _settings = settings;
    _repository = repository;
    _library = library;
    _sync = library.SyncedFolder;
    _paste = paste;
    _capture = capture;
    _pasteToApp = pasteToApp;
    _captureScreenshot = captureScreenshot;
    InitializeComponent();
    SortCombo.ItemsSource = new[] { "Newest first", "Oldest first", "Largest first" };
    SortCombo.SelectedIndex = 0;
    _focusPreviewMode = _settings.ImageLibraryFocusPreview;
    DetailPreview.PreparingFullscreen += (_, e) =>
    {
      if (_selected != null)
        e.Source = _selected.LoadFullImage(_library);
    };

    Loaded += (_, _) =>
    {
      ApplyLayoutMode();
      UpdateFolderUi();
    };
  }

  private void UpdateFolderUi()
  {
    AutoSaveBox.IsChecked = _settings.ImageLibraryAutoSaveEnabled;
    AutoSaveFolderPathText.Text = _library.DefaultAutoSaveFolder;
    SyncFolderBox.IsChecked = _settings.ImageLibrarySyncFolderEnabled;
    SyncFolderPathText.Text = string.IsNullOrWhiteSpace(_settings.ImageLibrarySyncFolderPath)
        ? "(no folder selected)"
        : _settings.ImageLibrarySyncFolderPath;
  }

  public bool TryCopySelectedImage()
  {
    if (_selected == null)
      return false;
    Copy_Click(this, new RoutedEventArgs());
    return true;
  }

  private void Screenshot_Click(object sender, RoutedEventArgs e) => _captureScreenshot?.Invoke();

  private void FocusPreviewToggle_Click(object sender, RoutedEventArgs e)
  {
    _focusPreviewMode = !_focusPreviewMode;
    _settings.ImageLibraryFocusPreview = _focusPreviewMode;
    _settings.Save();
    ApplyLayoutMode();
    if (_selected != null)
      SyncListSelection(_selected);
  }

  private void ApplyLayoutMode()
  {
    if (_focusPreviewMode)
    {
      LeftCol.Width = new GridLength(220);
      LeftCol.MinWidth = 160;
      RightCol.Width = new GridLength(1, GridUnitType.Star);
      RightCol.MinWidth = 420;
      ImageGridHost.Visibility = Visibility.Collapsed;
      ImageListHost.Visibility = Visibility.Visible;
      FocusPreviewToggle.Content = "⊟ Grid view";
    }
    else
    {
      LeftCol.Width = new GridLength(1, GridUnitType.Star);
      LeftCol.MinWidth = 320;
      RightCol.Width = new GridLength(360);
      RightCol.MinWidth = 280;
      ImageGridHost.Visibility = Visibility.Visible;
      ImageListHost.Visibility = Visibility.Collapsed;
      FocusPreviewToggle.Content = "⊞ Focus preview";
    }

    DetailPreview.ResetZoom();
  }

  public void Refresh()
  {
    UpdateFolderUi();

    var sort = SortCombo.SelectedIndex switch
    {
      1 => HistorySortMode.OldestFirst,
      2 => HistorySortMode.LargestFirst,
      _ => HistorySortMode.NewestFirst
    };

    var images = _library.GetImages(sort);
    if (FavoritesOnlyBox.IsChecked == true)
      images = images.Where(e => e.IsFavorite).ToList();
    if (AutoSavedOnlyBox.IsChecked == true)
      images = images.Where(e => !string.IsNullOrEmpty(e.ExportPath) && File.Exists(e.ExportPath)).ToList();

    var thumbSize = Math.Clamp(_settings.ImageLibraryThumbnailSize, 80, 220);
    var items = images.Select(e => ImageThumbItem.FromEntry(e, _library, thumbSize, _settings.ImageLibraryShowDimensionsOnThumb)).ToList();

    if (_sync.IsEnabled)
    {
      var linked = _sync.CollectLinkedPaths(images);
      foreach (var file in _sync.ScanFolderImages())
      {
        if (linked.Contains(file.FullPath))
          continue;
        items.Add(ImageThumbItem.FromSyncedFile(file, _library, thumbSize, _settings.ImageLibraryShowDimensionsOnThumb));
      }
    }

    _items = SortItems(items, sort);
    ImageGrid.ItemsSource = _items;
    ImageList.ItemsSource = _items;
    var syncNote = _sync.IsEnabled ? " · sync on" : "";
    ImageCountText.Text = $"{_items.Count} image(s){syncNote}";

    if (_selected != null)
    {
      var still = _items.FirstOrDefault(i => i.ItemKey == _selected.ItemKey);
      if (still != null) SelectItem(still);
      else ClearDetail();
    }
    else if (_items.Count > 0)
      SelectItem(_items[0]);
    else
      ClearDetail();
  }

  private static List<ImageThumbItem> SortItems(List<ImageThumbItem> items, HistorySortMode sort) =>
      sort switch
      {
        HistorySortMode.OldestFirst => items.OrderBy(i => i.SortDate).ToList(),
        HistorySortMode.LargestFirst => items.OrderByDescending(i => i.SizeBytes).ToList(),
        _ => items.OrderByDescending(i => i.SortDate).ToList()
      };

  private void SelectItem(ImageThumbItem item)
  {
    foreach (var i in _items) i.SelectionThickness = i == item ? new Thickness(2) : new Thickness(0);
    if (!_focusPreviewMode)
    {
      ImageGrid.ItemsSource = null;
      ImageGrid.ItemsSource = _items;
    }
    SyncListSelection(item);
    _selected = item;
    UpdateDetail();
  }

  private void SyncListSelection(ImageThumbItem item)
  {
    if (!_focusPreviewMode) return;
    _syncingListSelection = true;
    try
    {
      ImageList.SelectedItem = item;
      ImageList.ScrollIntoView(item);
    }
    finally { _syncingListSelection = false; }
  }

  private void ImageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (_syncingListSelection || !_focusPreviewMode) return;
    if (ImageList.SelectedItem is not ImageThumbItem item) return;
    if (_selected?.ItemKey == item.ItemKey) return;
    SelectItem(item);
  }

  private void UpdateDetail()
  {
    if (_selected == null) { ClearDetail(); return; }
    var image = _selected.LoadFullImage(_library);
    DetailTitle.Text = _selected.DisplayTitle;
    DetailPreview.SetSource(image);
    FavoriteOverlayBtn.Content = _selected.IsFavorite ? "★" : "☆";
    FavoriteOverlayBtn.IsEnabled = _selected.HasHistoryEntry;
    DetailMeta.Text = _selected.BuildMetaText(_settings.ImageLibraryAutoSaveEnabled);
    ShowPreviewChrome(visible: true);
    ResetChromeHideTimer();
  }

  private void ClearDetail()
  {
    _selected = null;
    _chromeHideTimer?.Stop();
    DetailTitle.Text = "Select an image";
    DetailPreview.SetSource(null);
    ShowPreviewChrome(visible: false);
    FavoriteOverlayBtn.IsEnabled = true;
    var hints = new List<string>();
    if (_settings.ImageLibraryAutoSaveEnabled)
      hints.Add($"Auto-save → {_library.DefaultAutoSaveFolder}");
    if (_sync.IsEnabled)
      hints.Add($"Sync folder → {_sync.SyncFolderPath}");
    DetailMeta.Text = hints.Count > 0
        ? string.Join("\n", hints)
        : "Copy an image to clipboard — it will appear here.";
  }

  private void Thumb_Click(object sender, MouseButtonEventArgs e)
  {
    if (sender is FrameworkElement { Tag: ImageThumbItem item })
      SelectItem(item);
  }

  private void Thumbs_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
  {
    var item = ClipboardContextMenuBuilder.FindParentDataContext<ImageThumbItem>(e.OriginalSource as DependencyObject)
        ?? (e.OriginalSource as FrameworkElement)?.Tag as ImageThumbItem;
    if (item != null)
      SelectItem(item);
  }

  private void Thumbs_ContextMenuOpening(object sender, ContextMenuEventArgs e)
  {
    if (_selected == null)
    {
      e.Handled = true;
      return;
    }
    var host = sender as FrameworkElement ?? this;
    host.ContextMenu = BuildImageContextMenu(_selected, includeRefresh: true);
  }

  private void DetailPreview_ContextMenuOpening(object sender, ContextMenuEventArgs e)
  {
    if (_selected == null)
    {
      e.Handled = true;
      return;
    }
    DetailPreview.ContextMenu = BuildImageContextMenu(_selected, includeRefresh: false);
  }

  private ContextMenu BuildImageContextMenu(ImageThumbItem item, bool includeRefresh) =>
      ClipboardContextMenuBuilder.Create(menu =>
          ClipboardContextMenuBuilder.AddImageLibraryMenu(menu, CreateImageMenuOptions(item, includeRefresh)));

  private ImageLibraryMenuOptions CreateImageMenuOptions(ImageThumbItem item, bool includeRefresh)
  {
    var syncPath = item.Entry?.SyncPath;
    var autoPath = item.Entry?.ExportPath;
    var storageActions = new ImageStorageMenuActions
    {
      OpenSyncFolder = _sync.IsEnabled ? () => OpenSyncFolder_Click(this, new RoutedEventArgs()) : null,
      OpenAutoSaveFolder = _settings.ImageLibraryAutoSaveEnabled
          ? () => ImageLibraryService.OpenFolder(_library.DefaultAutoSaveFolder)
          : null,
      ShowSyncedFile = ClipboardContextMenuBuilder.IsExistingFile(syncPath)
          ? () => ImageLibraryService.OpenInExplorer(syncPath!)
          : null,
      ShowAutoSavedFile = ClipboardContextMenuBuilder.IsExistingFile(autoPath)
          ? () => ImageLibraryService.OpenInExplorer(autoPath!)
          : null,
      CopySyncPath = ClipboardContextMenuBuilder.IsExistingFile(syncPath)
          ? () => CopyPathToClipboard(syncPath!)
          : null,
      CopyAutoSavePath = ClipboardContextMenuBuilder.IsExistingFile(autoPath)
          ? () => CopyPathToClipboard(autoPath!)
          : null
    };

    return new ImageLibraryMenuOptions
    {
      HasHistoryEntry = item.HasHistoryEntry,
      IsFolderOnly = item.IsFolderOnly,
      CanPaste = item.HasHistoryEntry,
      HasPreview = item.LoadFullImage(_library) != null,
      IsFavorite = item.IsFavorite,
      IsPinned = item.Entry?.IsPinned == true,
      OpenFilePath = item.GetOpenPath(),
      FolderOnlyPath = item.FilePath,
      ImageStorage = ClipboardContextMenuBuilder.BuildImageStorageInfo(
          _sync.IsEnabled,
          _settings.ImageLibraryAutoSaveEnabled,
          syncPath,
          autoPath,
          storageActions),
      Actions = new ImageLibraryMenuActions
      {
        PasteToApp = item.HasHistoryEntry ? () => PasteToApp_Click(this, new RoutedEventArgs()) : null,
        Copy = () => Copy_Click(this, new RoutedEventArgs()),
        SaveAs = item.HasHistoryEntry ? () => SaveAs_Click(this, new RoutedEventArgs()) : null,
        OpenFile = () => OpenFile_Click(this, new RoutedEventArgs()),
        Fullscreen = () => DetailPreview.OpenFullscreen(),
        ToggleFavorite = item.HasHistoryEntry ? () => Favorite_Click(this, new RoutedEventArgs()) : null,
        TogglePin = item.HasHistoryEntry ? () => Pin_Click(this, new RoutedEventArgs()) : null,
        RemoveFromClipboard = item.HasHistoryEntry ? () => RemoveFromClipboard(item) : null,
        DeleteSyncedFile = ClipboardContextMenuBuilder.IsExistingFile(syncPath)
            ? () => DeleteFileOnDisk(syncPath!, "Delete synced file",
                $"Delete this file from the sync folder?\n\n{syncPath}")
            : null,
        DeleteAutoSavedFile = ClipboardContextMenuBuilder.IsExistingFile(autoPath)
            ? () => DeleteFileOnDisk(autoPath!, "Delete auto-saved file",
                $"Delete the auto-saved copy?\n\n{autoPath}")
            : null,
        RemoveFromClipboardAndDeleteFiles = item.HasHistoryEntry ? () => RemoveFromClipboardAndDeleteFiles(item) : null,
        DeleteFolderOnlyFile = item.IsFolderOnly && item.FilePath != null
            ? () => DeleteFolderOnlyFile(item.FilePath!)
            : null,
        Refresh = includeRefresh ? Refresh : null
      }
    };
  }

  private void CopyPathToClipboard(string path)
  {
    try
    {
      using (_capture.SuppressCapture())
        System.Windows.Clipboard.SetText(path);
    }
    catch { }
  }

  private void RemoveFromClipboard(ImageThumbItem item)
  {
    if (!item.HasHistoryEntry || item.Entry == null) return;
    if (!AppDialog.Confirm(
            "Remove this image from clipboard history?\n\nFiles on disk (sync folder or auto-save) are kept.",
            "Remove from clipboard", DialogOwner))
      return;
    _repository.Delete(item.Entry.Id);
    AfterLibraryMutation();
  }

  private void RemoveFromClipboardAndDeleteFiles(ImageThumbItem item)
  {
    if (!item.HasHistoryEntry || item.Entry == null) return;
    var paths = new List<string>();
    if (ClipboardContextMenuBuilder.IsExistingFile(item.Entry.SyncPath))
      paths.Add(item.Entry.SyncPath!);
    if (ClipboardContextMenuBuilder.IsExistingFile(item.Entry.ExportPath))
      paths.Add(item.Entry.ExportPath!);
    var detail = paths.Count > 0
        ? "\n\nAlso delete:\n" + string.Join("\n", paths)
        : "";
    if (!AppDialog.ConfirmWarning(
            "Remove from clipboard history and delete linked file(s) from disk?" + detail,
            "Remove and delete", DialogOwner))
      return;
    foreach (var path in paths)
      TryDeleteFile(path);
    _repository.Delete(item.Entry.Id);
    AfterLibraryMutation();
  }

  private void DeleteFileOnDisk(string path, string title, string message)
  {
    if (!File.Exists(path)) return;
    if (!AppDialog.ConfirmWarning(message, title, DialogOwner))
      return;
    if (!TryDeleteFile(path)) return;
    if (_selected?.Entry != null)
    {
      if (string.Equals(_selected.Entry.SyncPath, path, StringComparison.OrdinalIgnoreCase))
      {
        _repository.SetSyncPath(_selected.Entry.Id, "");
        _selected.Entry.SyncPath = null;
      }
      if (string.Equals(_selected.Entry.ExportPath, path, StringComparison.OrdinalIgnoreCase))
      {
        _repository.SetExportPath(_selected.Entry.Id, "");
        _selected.Entry.ExportPath = null;
      }
    }
    AfterLibraryMutation();
  }

  private void DeleteFolderOnlyFile(string path)
  {
    if (!File.Exists(path)) return;
    if (!AppDialog.ConfirmWarning(
            $"Delete this file from the sync folder?\n\n{path}",
            "Delete from disk", DialogOwner))
      return;
    if (!TryDeleteFile(path)) return;
    AfterLibraryMutation();
  }

  private bool TryDeleteFile(string path)
  {
    try
    {
      File.Delete(path);
      return true;
    }
    catch (Exception ex)
    {
      AppDialog.Error($"Could not delete file:\n{ex.Message}", "Delete", DialogOwner);
      return false;
    }
  }

  private void AfterLibraryMutation()
  {
    Refresh();
    LibraryChanged?.Invoke(this, EventArgs.Empty);
  }

  private void PreviewChrome_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
  {
    if (_selected == null)
      return;
    ResetChromeHideTimer();
  }

  private void PreviewChrome_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
  {
    if (_selected == null)
      return;
    ResetChromeHideTimer();
  }

  private void ResetChromeHideTimer()
  {
    ShowPreviewChrome(visible: true);
    _chromeHideTimer ??= new DispatcherTimer { Interval = ChromeHideDelay };
    _chromeHideTimer.Stop();
    _chromeHideTimer.Tick -= ChromeHideTimer_Tick;
    _chromeHideTimer.Tick += ChromeHideTimer_Tick;
    _chromeHideTimer.Start();
  }

  private void ChromeHideTimer_Tick(object? sender, EventArgs e)
  {
    _chromeHideTimer?.Stop();
    ShowPreviewChrome(visible: false, animateHide: true);
  }

  private void ShowPreviewChrome(bool visible, bool animateHide = false)
  {
    ActionOverlay.BeginAnimation(UIElement.OpacityProperty, null);

    if (visible)
    {
      ActionOverlay.Opacity = 1;
      ActionOverlay.IsHitTestVisible = true;
      DetailPreview.SetChromeVisible(true);
      return;
    }

    if (!animateHide)
    {
      ActionOverlay.Opacity = 0;
      ActionOverlay.IsHitTestVisible = false;
      DetailPreview.SetChromeVisible(false);
      return;
    }

    ActionOverlay.IsHitTestVisible = true;
    var fadeOut = new DoubleAnimation(ActionOverlay.Opacity, 0, ChromeFadeOutDuration)
    {
      EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
    };
    fadeOut.Completed += (_, _) => ActionOverlay.IsHitTestVisible = false;
    ActionOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    DetailPreview.AnimateChromeHide(ChromeFadeOutDuration.TimeSpan);
  }

  private void ListScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
  {
    if (sender is not DependencyObject dep) return;
    var scroll = FindVisualChild<ScrollViewer>(dep);
    if (scroll == null) return;
    scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta / 3.0);
    e.Handled = true;
  }

  private void AutoSave_Changed(object sender, RoutedEventArgs e)
  {
    _settings.ImageLibraryAutoSaveEnabled = AutoSaveBox.IsChecked == true;
    _settings.Save();
    UpdateFolderUi();
  }

  private void SyncFolder_Changed(object sender, RoutedEventArgs e)
  {
    var enable = SyncFolderBox.IsChecked == true;
    if (enable && string.IsNullOrWhiteSpace(_settings.ImageLibrarySyncFolderPath))
    {
      if (!TryPickSyncFolder())
      {
        SyncFolderBox.IsChecked = false;
        return;
      }
    }

    _settings.ImageLibrarySyncFolderEnabled = enable;
    _settings.Save();
    UpdateFolderUi();
    Refresh();
  }

  private bool TryPickSyncFolder()
  {
    using var dlg = new Forms.FolderBrowserDialog
    {
      Description = "Choose a folder to sync with the image library",
      SelectedPath = string.IsNullOrWhiteSpace(_settings.ImageLibrarySyncFolderPath)
          ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
          : _settings.ImageLibrarySyncFolderPath,
      ShowNewFolderButton = true
    };
    if (dlg.ShowDialog() != Forms.DialogResult.OK)
      return false;
    _settings.ImageLibrarySyncFolderPath = dlg.SelectedPath;
    _settings.Save();
    UpdateFolderUi();
    return true;
  }

  private void ChooseAutoSaveFolder_Click(object sender, RoutedEventArgs e)
  {
    using var dlg = new Forms.FolderBrowserDialog
    {
      Description = "Folder for auto-saving clipboard images",
      SelectedPath = _library.DefaultAutoSaveFolder,
      ShowNewFolderButton = true
    };
    if (dlg.ShowDialog() != Forms.DialogResult.OK) return;
    _settings.ImageLibraryAutoSaveFolder = dlg.SelectedPath;
    _settings.Save();
    UpdateFolderUi();
  }

  private void ChooseSyncFolder_Click(object sender, RoutedEventArgs e)
  {
    if (!TryPickSyncFolder())
      return;
    _settings.ImageLibrarySyncFolderEnabled = true;
    SyncFolderBox.IsChecked = true;
    _settings.Save();
    UpdateFolderUi();
    Refresh();
  }

  private void OpenAutoSaveFolder_Click(object sender, RoutedEventArgs e) =>
      ImageLibraryService.OpenFolder(_library.DefaultAutoSaveFolder);

  private void OpenSyncFolder_Click(object sender, RoutedEventArgs e)
  {
    if (string.IsNullOrWhiteSpace(_settings.ImageLibrarySyncFolderPath))
    {
      ChooseSyncFolder_Click(sender, e);
      return;
    }
    ImageLibraryService.OpenFolder(_sync.SyncFolderPath);
  }

  private void AutoSaveInfo_Click(object sender, RoutedEventArgs e) =>
      AppDialog.Info(
          "Auto-save optionally copies each new clipboard image to a separate folder, using its own naming rules.\n\n"
          + "It does not import existing files from that folder into the library.",
          "Auto-save images",
          DialogOwner);

  private void SyncInfo_Click(object sender, RoutedEventArgs e) =>
      AppDialog.Info(
          "Sync folder links the image library to one folder on your PC:\n\n"
          + "• Every image already in that folder appears in the library.\n"
          + "• Every new clipboard image is saved into that folder.\n\n"
          + "This is different from Auto-save, which only saves copies of new captures and does not show files that were already on disk.",
          "Sync to folder",
          DialogOwner);

  private void Filter_Changed(object sender, RoutedEventArgs e) => Refresh();
  private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

  private void SaveAs_Click(object sender, RoutedEventArgs e)
  {
    if (_selected == null || !_selected.HasHistoryEntry) return;
    var ext = _settings.ImageLibrarySaveFormat.TrimStart('.').ToLowerInvariant();
    var dlg = new SaveFileDialog
    {
      Filter = ext == "jpg" ? "JPEG|*.jpg|PNG|*.png" : "PNG|*.png|JPEG|*.jpg",
      FileName = $"clipboard_{_selected.Entry!.CapturedAt:yyyy-MM-dd_HH-mm-ss}.{ext}",
      DefaultExt = ext
    };
    if (dlg.ShowDialog() != true) return;
    if (_library.SaveAs(_selected.Entry, dlg.FileName) != null)
      AppDialog.Info($"Saved:\n{dlg.FileName}", "Image library", DialogOwner);
  }

  private void Copy_Click(object sender, RoutedEventArgs e)
  {
    if (_selected == null) return;
    if (_selected.HasHistoryEntry && _selected.Entry != null)
      _library.CopyToClipboard(_selected.Entry, _paste, _capture);
    else if (_selected.FilePath != null)
      _library.CopyFileToClipboard(_selected.FilePath, _capture);
  }

  private void PasteToApp_Click(object sender, RoutedEventArgs e)
  {
    if (_selected == null || !_selected.HasHistoryEntry || _selected.Entry == null) return;
    _pasteToApp?.Invoke(_selected.Entry);
  }

  private void OpenFile_Click(object sender, RoutedEventArgs e)
  {
    if (_selected == null) return;
    var path = _selected.GetOpenPath();
    if (!string.IsNullOrEmpty(path) && File.Exists(path))
      ImageLibraryService.OpenInExplorer(path);
  }

  private void Favorite_Click(object sender, RoutedEventArgs e)
  {
    if (_selected == null || !_selected.HasHistoryEntry || _selected.Entry == null) return;
    _repository.SetFavorite(_selected.Entry.Id, !_selected.Entry.IsFavorite);
    _selected.Entry.IsFavorite = !_selected.Entry.IsFavorite;
    Refresh();
    LibraryChanged?.Invoke(this, EventArgs.Empty);
  }

  private void Pin_Click(object sender, RoutedEventArgs e)
  {
    if (_selected == null || !_selected.HasHistoryEntry || _selected.Entry == null) return;
    _repository.SetPinned(_selected.Entry.Id, !_selected.Entry.IsPinned);
    _selected.Entry.IsPinned = !_selected.Entry.IsPinned;
    Refresh();
    LibraryChanged?.Invoke(this, EventArgs.Empty);
  }

  private void Delete_Click(object sender, RoutedEventArgs e)
  {
    if (_selected == null) return;
    if (_selected.HasHistoryEntry)
      RemoveFromClipboard(_selected);
    else if (_selected.FilePath != null)
      DeleteFolderOnlyFile(_selected.FilePath);
  }

  private void ExportAll_Click(object sender, RoutedEventArgs e)
  {
    using var dlg = new Forms.FolderBrowserDialog
    {
      Description = "Export all library images to this folder",
      ShowNewFolderButton = true
    };
    if (dlg.ShowDialog() != Forms.DialogResult.OK) return;
    var n = 0;
    foreach (var item in _items)
    {
      string dest;
      if (item.HasHistoryEntry && item.Entry != null)
      {
        dest = Path.Combine(dlg.SelectedPath, $"{item.Entry.CapturedAt:yyyy-MM-dd_HHmmss}_{item.Entry.Id}.png");
        dest = Path.GetFullPath(dest);
        if (_library.SaveAs(item.Entry, dest) != null) n++;
      }
      else if (item.FilePath != null)
      {
        dest = Path.Combine(dlg.SelectedPath, Path.GetFileName(item.FilePath));
        dest = Path.GetFullPath(dest);
        try
        {
          File.Copy(item.FilePath, dest, overwrite: false);
          n++;
        }
        catch { }
      }
    }
    AppDialog.Info($"Exported {n} image(s) to:\n{dlg.SelectedPath}", "Export", DialogOwner);
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

  private static string FormatSize(long b)
  {
    if (b < 1024) return $"{b} B";
    if (b < 1024 * 1024) return $"{b / 1024.0:F1} KB";
    return $"{b / (1024.0 * 1024):F1} MB";
  }

  public sealed class ImageThumbItem
  {
    private ImageThumbItem() { }

    public ClipboardEntry? Entry { get; private set; }
    public string? FilePath { get; private set; }
    public BitmapImage? Thumbnail { get; private set; }
    public BitmapImage? ListThumbnail { get; private set; }
    public string Title { get; private set; } = "";
    public string Subtitle { get; private set; } = "";
    public string Caption { get; private set; } = "";
    public double ThumbSize { get; private set; }
    public DateTime SortDate { get; private set; }
    public long SizeBytes { get; private set; }
    public Thickness SelectionThickness { get; set; }

    public string ItemKey => Entry != null ? $"db:{Entry.Id}" : $"file:{FilePath}";
    public bool HasHistoryEntry => Entry != null;
    public bool IsFolderOnly => Entry == null && FilePath != null;
    public bool IsFavorite => Entry?.IsFavorite == true;
    public string DisplayTitle { get; private set; } = "";

    public static ImageThumbItem FromEntry(ClipboardEntry entry, ImageLibraryService library, double thumbSize, bool showCaption)
    {
      var title = ImageDisplayHelper.FormatTitle(entry);
      return new ImageThumbItem
      {
        Entry = entry,
        ThumbSize = thumbSize,
        Thumbnail = library.LoadThumbnail(entry, (int)thumbSize),
        ListThumbnail = library.LoadThumbnail(entry, 96),
        SortDate = entry.CapturedAt,
        SizeBytes = entry.SizeBytes,
        Title = title,
        DisplayTitle = title,
        Subtitle = $"{entry.TimeAgo} · {FormatSize(entry.SizeBytes)}",
        Caption = showCaption ? title : ""
      };
    }

    public static ImageThumbItem FromSyncedFile(SyncedFolderService.SyncedFolderFile file, ImageLibraryService library, double thumbSize, bool showCaption)
    {
      var name = Path.GetFileNameWithoutExtension(file.FullPath);
      var img = library.LoadThumbnailFromPath(file.FullPath, (int)thumbSize);
      var dims = img != null ? $" ({img.PixelWidth}×{img.PixelHeight})" : "";
      var title = $"{name}{dims}";
      return new ImageThumbItem
      {
        FilePath = file.FullPath,
        ThumbSize = thumbSize,
        Thumbnail = img ?? library.LoadThumbnailFromPath(file.FullPath, (int)thumbSize),
        ListThumbnail = library.LoadThumbnailFromPath(file.FullPath, 96),
        Title = title,
        DisplayTitle = title,
        Subtitle = $"{FormatFileAge(file.ModifiedUtc)} · {FormatSize(file.SizeBytes)} · Synced folder",
        Caption = showCaption ? title : "",
        SortDate = file.ModifiedUtc.ToLocalTime(),
        SizeBytes = file.SizeBytes
      };
    }

    public BitmapImage? LoadFullImage(ImageLibraryService library) =>
        library.LoadImageForEntry(Entry, FilePath);

    public string? GetOpenPath()
    {
      if (FilePath != null) return FilePath;
      if (Entry == null) return null;
      if (!string.IsNullOrEmpty(Entry.SyncPath) && File.Exists(Entry.SyncPath)) return Entry.SyncPath;
      if (!string.IsNullOrEmpty(Entry.ExportPath) && File.Exists(Entry.ExportPath)) return Entry.ExportPath;
      return Entry.PayloadPath;
    }

    public string BuildMetaText(bool autoSaveEnabled)
    {
      if (IsFolderOnly && FilePath != null)
        return $"{FormatFileAge(SortDate)} · {FormatSize(SizeBytes)}\nSynced folder file\n{FilePath}";

      if (Entry == null) return "";
      var lines = new List<string> { $"{Entry.TimeAgo} · {FormatSize(Entry.SizeBytes)}" };
      if (!string.IsNullOrEmpty(Entry.SourceApp))
        lines[0] += $" · from {Entry.SourceApp}";
      if (!string.IsNullOrEmpty(Entry.SyncPath) && File.Exists(Entry.SyncPath))
        lines.Add($"Synced: {Entry.SyncPath}");
      if (!string.IsNullOrEmpty(Entry.ExportPath) && File.Exists(Entry.ExportPath))
        lines.Add($"Auto-saved: {Entry.ExportPath}");
      else if (autoSaveEnabled)
        lines.Add("Not auto-saved yet");
      if (Entry.IsPinned) lines.Add("Pinned");
      if (Entry.IsFavorite) lines.Add("★ Favorite");
      return string.Join("\n", lines);
    }

    private static string FormatFileAge(DateTime utcOrLocal)
    {
      var span = DateTime.Now - utcOrLocal;
      if (span.TotalMinutes < 1) return "Just now";
      if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
      if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
      if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
      return utcOrLocal.ToString("MMM d, yyyy");
    }
  }
}
