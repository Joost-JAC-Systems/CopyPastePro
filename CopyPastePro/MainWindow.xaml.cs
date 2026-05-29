using System.ComponentModel;
using System.IO;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CopyPastePro.Models;
using CopyPastePro.Services;
using CopyPastePro.Views;

namespace CopyPastePro;

public partial class MainWindow : Window
{
  private readonly ClipboardHistoryRepository _repository;
  private readonly ClipboardCaptureService _capture;
  private readonly ClipboardPasteService _paste;
  private readonly AppSettings _settings;
  private readonly CategoryClassifier _classifier;
  private readonly AutoBackupService _backup;
  private readonly PrivacyService _privacy;
  private readonly ClipboardQuickActionsService _quickActions;
  private readonly Action? _openQuickAccess;
  private readonly SettingsView _settingsView;
  private readonly ImageLibraryView _imageLibraryView;
  private readonly ResourceThrottleService? _throttle;
  private Action? _captureScreenshot;
  private List<ClipboardEntry> _entries = new();
  private bool _isReady;
  private double _uiScale = 1.0;

  public MainWindow(
      ClipboardHistoryRepository repository,
      ClipboardCaptureService capture,
      ClipboardPasteService paste,
      AppSettings settings,
      CategoryClassifier classifier,
      AutoBackupService backup,
      PrivacyService privacy,
      SensitiveRetentionService? sensitiveRetention = null,
      ImageLibraryService? imageLibrary = null,
      Action? openQuickAccess = null,
      ClipboardQuickActionsService? quickActions = null,
      ResourceThrottleService? throttle = null)
  {
    _repository = repository;
    _capture = capture;
    _paste = paste;
    _settings = settings;
    _classifier = classifier;
    _backup = backup;
    _privacy = privacy;
    _quickActions = quickActions ?? new ClipboardQuickActionsService(_settings, _repository, _capture, _paste, _privacy, RefreshList);
    _openQuickAccess = openQuickAccess;
    _throttle = throttle;
    InitializeComponent();
    IsVisibleChanged += (_, _) => UpdatePreview();
    StateChanged += (_, _) => UpdatePreview();

    var lib = imageLibrary ?? new ImageLibraryService(_settings, _repository);
    _imageLibraryView = new ImageLibraryView(_settings, _repository, lib, _paste, _capture, PasteToActiveApp, () => _captureScreenshot?.Invoke());
    _imageLibraryView.LibraryChanged += (_, _) => RefreshList();
    ImageLibraryHost.Children.Add(_imageLibraryView);

    _settingsView = new SettingsView(_settings, _classifier, _repository, _privacy, sensitiveRetention, _quickActions);
    _settingsView.SettingsSaved += (_, _) =>
    {
      UpdateFooter();
      RefreshList();
      _imageLibraryView.ApplySettingsFromSaved();
    };
    _settingsView.HotkeysChanged += (_, _) => HotkeysChanged?.Invoke(this, EventArgs.Empty);
    SettingsHost.Children.Add(_settingsView);

    InitComboBoxes();
    _uiScale = MainWindowUiScale.Clamp(_settings.MainWindowUiScale);
    ApplyUiScale(save: false);
    _isReady = true;
    UpdateFooter();
    Loaded += (_, _) => RefreshList();
  }

  private void HistoryList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) =>
      ClipboardContextMenuBuilder.PrepareListBoxRightClick(HistoryList, e);

  private void HistoryList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
  {
    var entry = SelectedEntry;
    if (entry == null)
    {
      HistoryList.ContextMenu = BuildHistoryListEmptyContextMenu();
      return;
    }
    HistoryList.ContextMenu = BuildHistoryEntryContextMenu(entry);
  }

  private void PreviewText_ContextMenuOpening(object sender, ContextMenuEventArgs e)
  {
    if (PreviewText.Visibility != Visibility.Visible || string.IsNullOrEmpty(PreviewText.Text))
    {
      e.Handled = true;
      return;
    }
    PreviewText.ContextMenu = ClipboardContextMenuBuilder.Create(menu =>
        ClipboardContextMenuBuilder.AddTextPreviewItems(menu, new TextPreviewActions
        {
          Copy = () =>
          {
            using (_capture.SuppressCapture())
              System.Windows.Clipboard.SetText(PreviewText.Text);
            StatusText.Text = "● Copied";
          },
          SelectAll = () => { PreviewText.SelectAll(); PreviewText.Focus(); }
        }));
  }

  private void PreviewImageZoom_ContextMenuOpening(object sender, ContextMenuEventArgs e)
  {
    var entry = SelectedEntry;
    if (entry?.ContentType != ClipboardContentType.Image)
    {
      e.Handled = true;
      return;
    }
    PreviewImageZoom.ContextMenu = BuildHistoryImagePreviewMenu(entry);
  }

  private ContextMenu BuildHistoryEntryContextMenu(ClipboardEntry entry) =>
      ClipboardContextMenuBuilder.Create(menu =>
          ClipboardContextMenuBuilder.AddClipboardEntryMenu(menu, CreateEntryMenuOptions(entry)));

  private ContextMenu BuildHistoryImagePreviewMenu(ClipboardEntry entry) =>
      ClipboardContextMenuBuilder.Create(menu =>
          ClipboardContextMenuBuilder.AddClipboardEntryMenu(menu, CreateEntryMenuOptions(entry, includeFullscreen: true)));

  private ContextMenu BuildHistoryListEmptyContextMenu() =>
      ClipboardContextMenuBuilder.Create(menu =>
          menu.Items.Add(ClipboardContextMenuBuilder.Item("Refresh list", RefreshList, icon: "↻")));

  private ClipboardEntryMenuOptions CreateEntryMenuOptions(ClipboardEntry entry, bool includeFullscreen = false) => new()
  {
    IsFavorite = entry.IsFavorite,
    IsPinned = entry.IsPinned,
    IsImage = entry.ContentType == ClipboardContentType.Image,
    HasImageFile = entry.ContentType == ClipboardContentType.Image && GetImageFilePath(entry) != null,
    ShowOpenManager = false,
    ImageStorage = BuildHistoryImageStorage(entry),
    Actions = new ClipboardEntryMenuActions
    {
      PasteToApp = () => PasteToActiveApp(entry),
      Copy = () =>
      {
        using (_capture.SuppressCapture()) _paste.SetClipboard(entry);
        StatusText.Text = "● Copied";
      },
      Fullscreen = includeFullscreen && entry.ContentType == ClipboardContentType.Image
          ? () => PreviewImageZoom.OpenFullscreen()
          : null,
      ExportImageAs = entry.ContentType == ClipboardContentType.Image
          ? () => ExportSelectedImage(entry)
          : null,
      OpenImageFile = entry.ContentType == ClipboardContentType.Image
          ? () => OpenImageFile(entry)
          : null,
      ToggleFavorite = () =>
      {
        _repository.SetFavorite(entry.Id, !entry.IsFavorite);
        RefreshList();
      },
      TogglePin = () =>
      {
        _repository.SetPinned(entry.Id, !entry.IsPinned);
        RefreshList();
      },
      RemoveFromClipboard = () => RemoveEntryFromClipboard(entry, confirmKeepFiles: true),
      DeleteSyncedFile = ClipboardContextMenuBuilder.IsExistingFile(entry.SyncPath)
          ? () => DeleteEntryFileOnDisk(entry, entry.SyncPath!, "Delete synced file",
              $"Delete this file from the sync folder?\n\n{entry.SyncPath}")
          : null,
      DeleteAutoSavedFile = ClipboardContextMenuBuilder.IsExistingFile(entry.ExportPath)
          ? () => DeleteEntryFileOnDisk(entry, entry.ExportPath!, "Delete auto-saved file",
              $"Delete the auto-saved copy?\n\n{entry.ExportPath}")
          : null,
      RemoveFromClipboardAndDeleteFiles = entry.ContentType == ClipboardContentType.Image
          ? () => RemoveEntryAndLinkedFiles(entry)
          : null
    }
  };

  private void RemoveEntryFromClipboard(ClipboardEntry entry, bool confirmKeepFiles)
  {
    if (confirmKeepFiles && entry.ContentType == ClipboardContentType.Image
        && (ClipboardContextMenuBuilder.IsExistingFile(entry.SyncPath)
            || ClipboardContextMenuBuilder.IsExistingFile(entry.ExportPath)))
    {
      if (!AppDialog.Confirm(
              "Remove from clipboard history?\n\nFiles in sync folder or auto-save are kept.",
              "Remove from clipboard", this))
        return;
    }
    else if (!AppDialog.Confirm(
                 "Remove this item from clipboard history?",
                 "Remove from clipboard", this))
      return;

    _repository.Delete(entry.Id);
    RefreshList();
    UpdatePreview();
  }

  private void DeleteEntryFileOnDisk(ClipboardEntry entry, string path, string title, string message)
  {
    if (!File.Exists(path)) return;
    if (!AppDialog.ConfirmWarning(message, title, this))
      return;
    try
    {
      File.Delete(path);
      if (string.Equals(entry.SyncPath, path, StringComparison.OrdinalIgnoreCase))
      {
        _repository.SetSyncPath(entry.Id, "");
        entry.SyncPath = null;
      }
      if (string.Equals(entry.ExportPath, path, StringComparison.OrdinalIgnoreCase))
      {
        _repository.SetExportPath(entry.Id, "");
        entry.ExportPath = null;
      }
      RefreshList();
      UpdatePreview();
    }
    catch (Exception ex)
    {
      AppDialog.Error($"Could not delete file:\n{ex.Message}", title, this);
    }
  }

  private void RemoveEntryAndLinkedFiles(ClipboardEntry entry)
  {
    var paths = new List<string>();
    if (ClipboardContextMenuBuilder.IsExistingFile(entry.SyncPath)) paths.Add(entry.SyncPath!);
    if (ClipboardContextMenuBuilder.IsExistingFile(entry.ExportPath)) paths.Add(entry.ExportPath!);
    var detail = paths.Count > 0 ? "\n\nAlso delete:\n" + string.Join("\n", paths) : "";
    if (!AppDialog.ConfirmWarning(
            "Remove from clipboard history and delete linked file(s) from disk?" + detail,
            "Remove and delete", this))
      return;
    foreach (var path in paths)
    {
      try { File.Delete(path); } catch { }
    }
    _repository.Delete(entry.Id);
    RefreshList();
    UpdatePreview();
  }

  private ImageStorageMenuInfo BuildHistoryImageStorage(ClipboardEntry entry)
  {
    if (entry.ContentType != ClipboardContentType.Image)
      return new ImageStorageMenuInfo();

    return ClipboardContextMenuBuilder.BuildImageStorageInfo(
        _settings.ImageLibrarySyncFolderEnabled,
        _settings.ImageLibraryAutoSaveEnabled,
        entry.SyncPath,
        entry.ExportPath,
        new ImageStorageMenuActions
        {
          OpenSyncFolder = _settings.ImageLibrarySyncFolderEnabled && !string.IsNullOrWhiteSpace(_settings.ImageLibrarySyncFolderPath)
              ? () => ImageLibraryService.OpenFolder(_settings.ImageLibrarySyncFolderPath)
              : null,
          OpenAutoSaveFolder = _settings.ImageLibraryAutoSaveEnabled
              ? () => ImageLibraryService.OpenFolder(_imageLibraryView.DefaultAutoSaveFolder)
              : null,
          ShowSyncedFile = ClipboardContextMenuBuilder.IsExistingFile(entry.SyncPath)
              ? () => ImageLibraryService.OpenInExplorer(entry.SyncPath!)
              : null,
          ShowAutoSavedFile = ClipboardContextMenuBuilder.IsExistingFile(entry.ExportPath)
              ? () => ImageLibraryService.OpenInExplorer(entry.ExportPath!)
              : null,
          CopySyncPath = ClipboardContextMenuBuilder.IsExistingFile(entry.SyncPath)
              ? () => CopyTextToClipboard(entry.SyncPath!)
              : null,
          CopyAutoSavePath = ClipboardContextMenuBuilder.IsExistingFile(entry.ExportPath)
              ? () => CopyTextToClipboard(entry.ExportPath!)
              : null
        });
  }

  private static string? GetImageFilePath(ClipboardEntry entry)
  {
    if (ClipboardContextMenuBuilder.IsExistingFile(entry.SyncPath)) return entry.SyncPath;
    if (ClipboardContextMenuBuilder.IsExistingFile(entry.ExportPath)) return entry.ExportPath;
    if (!string.IsNullOrEmpty(entry.PayloadPath) && File.Exists(entry.PayloadPath)) return entry.PayloadPath;
    return null;
  }

  private void OpenImageFile(ClipboardEntry entry)
  {
    var path = GetImageFilePath(entry);
    if (path != null)
      ImageLibraryService.OpenInExplorer(path);
  }

  private void CopyTextToClipboard(string text)
  {
    try
    {
      using (_capture.SuppressCapture())
        System.Windows.Clipboard.SetText(text);
      StatusText.Text = "● Copied path";
    }
    catch { }
  }

  public event EventHandler? HotkeysChanged;

  public void SetScreenshotCapture(Action capture) => _captureScreenshot = capture;

  private void InitComboBoxes()
  {
    SortCombo.ItemsSource = Enum.GetNames<HistorySortMode>();
    SortCombo.SelectedItem = _settings.DefaultSort.ToString();
    RefreshCategoryCombo();
  }

  private void RefreshCategoryCombo()
  {
    var sel = CategoryCombo.SelectedItem as string;
    CategoryCombo.ItemsSource = _classifier.AllCategories();
    CategoryCombo.SelectedItem = sel ?? "All";
    if (CategoryCombo.SelectedItem == null) CategoryCombo.SelectedIndex = 0;
  }

  public void RefreshList()
  {
    if (!_isReady) return;
    if (ImageLibraryHost.Visibility == Visibility.Visible)
      _imageLibraryView.Refresh();

    var sort = Enum.TryParse<HistorySortMode>(SortCombo.SelectedItem as string, out var s)
        ? s : _settings.DefaultSort;

    var q = new HistoryQuery
    {
      Search = SearchBox.Text.Trim(),
      ContentType = FilterCombo.SelectedIndex switch
      {
        1 => ClipboardContentType.Text,
        2 => ClipboardContentType.Image,
        3 => ClipboardContentType.Files,
        4 => ClipboardContentType.Html,
        5 => ClipboardContentType.Rtf,
        6 => ClipboardContentType.Other,
        _ => null
      },
      Category = CategoryCombo.SelectedItem as string,
      FavoritesOnly = FavoritesOnlyBox.IsChecked == true,
      Sort = sort
    };

    _entries = _repository.Query(q);
    var items = _entries.Select(e => new HistoryListItem(e, _settings.ShowCategoryBadges, _privacy)).ToList();

    if (GroupByCategoryBox.IsChecked == true)
    {
      HistoryList.GroupStyle.Clear();
      if (System.Windows.Application.Current.TryFindResource("CategoryGroupStyle") is GroupStyle gs)
        HistoryList.GroupStyle.Add(gs);
      var view = new ListCollectionView(items);
      view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(HistoryListItem.GroupKey)));
      HistoryList.ItemsSource = view;
    }
    else
    {
      HistoryList.GroupStyle.Clear();
      HistoryList.ItemsSource = items;
    }

    var counts = _repository.GetCategoryCounts();
    StatsText.Text = counts.Count > 0
        ? string.Join(" · ", counts.Take(6).Select(c => $"{c.Key}: {c.Value}"))
        : "No clips yet — copy something!";
    Title = $"CopyPaste Pro — {_entries.Count} items";
    RefreshCategoryCombo();
  }

  private ClipboardEntry? SelectedEntry =>
      (HistoryList.SelectedItem as HistoryListItem)?.Entry;

  private void ShowHistory()
  {
    PageTitle.Text = "Clipboard";
    HistoryPanel.Visibility = Visibility.Visible;
    SettingsHost.Visibility = Visibility.Collapsed;
    ImageLibraryHost.Visibility = Visibility.Collapsed;
    NavHistory.IsChecked = true;
    UpdateCaptureButtonLabel();
    UpdateFooter();
  }

  private void ShowImageLibrary()
  {
    PageTitle.Text = "Image library";
    HistoryPanel.Visibility = Visibility.Collapsed;
    SettingsHost.Visibility = Visibility.Collapsed;
    ImageLibraryHost.Visibility = Visibility.Visible;
    NavImages.IsChecked = true;
    _imageLibraryView.Refresh();
    var count = _repository.Query(new HistoryQuery { ContentType = ClipboardContentType.Image }).Count;
    var syncNote = _settings.ImageLibrarySyncFolderEnabled ? " · Sync: on" : "";
    StatsText.Text = count > 0
        ? $"{count} images in clipboard history · Auto-save: {(_settings.ImageLibraryAutoSaveEnabled ? "on" : "off")}{syncNote}"
        : "Copy an image — it will appear here";
  }

  private void ShowSettings()
  {
    PageTitle.Text = "Settings";
    HistoryPanel.Visibility = Visibility.Collapsed;
    ImageLibraryHost.Visibility = Visibility.Collapsed;
    SettingsHost.Visibility = Visibility.Visible;
    NavSettings.IsChecked = true;
  }

  private void NavHistory_Click(object sender, RoutedEventArgs e) => ShowHistory();
  private void NavImages_Click(object sender, RoutedEventArgs e) => ShowImageLibrary();
  private void NavSettings_Click(object sender, RoutedEventArgs e) => ShowSettings();
  public void OpenImageLibraryPanel() { Show(); ShowImageLibrary(); }
  public void OpenSettingsPanel() { Show(); ShowSettings(); }

  private void OpenQuickAccess_Click(object sender, RoutedEventArgs e) => _openQuickAccess?.Invoke();

  private void BackupNow_Click(object sender, RoutedEventArgs e)
  {
    var path = _backup.RunBackup();
    StatusText.Text = string.IsNullOrEmpty(path) ? "● No backup" : "● Backed up";
    AppDialog.Info(string.IsNullOrEmpty(path) ? "Nothing to backup yet." : $"Saved:\n{path}", "Backup", this);
  }

  private void Recategorize_Click(object sender, RoutedEventArgs e)
  {
    _repository.RecategorizeAll(_classifier.Classify);
    RefreshList();
    StatusText.Text = "● Recategorized";
  }

  private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

  private bool IsRichFormattingActive =>
      IsVisible
      && WindowState != WindowState.Minimized
      && HistoryPanel.Visibility == Visibility.Visible
      && (_throttle?.IsFormattingAllowed ?? true);

  private void ExportSelectedImage(ClipboardEntry entry)
  {
    if (string.IsNullOrEmpty(entry.PayloadPath)) return;
    var bytes = _repository.ReadPayload(entry.PayloadPath, _settings.EncryptPayloadFiles);
    if (bytes == null) return;
    if (ImageExportService.PromptAndExport(this, bytes, "clipboard_image"))
      StatusText.Text = "● Image exported";
  }

  private void UpdatePreview()
  {
    PreviewImageZoom.Visibility = Visibility.Collapsed;
    PreviewContentScroll.Visibility = Visibility.Visible;
    PreviewText.Visibility = Visibility.Collapsed;
    FormattedPreview.Visibility = Visibility.Collapsed;
    PreviewFiles.Visibility = Visibility.Collapsed;
    PreviewImageZoom.SetSource(null);
    PreviewText.Text = string.Empty;
    FormattedPreview.Clear();

    var entry = SelectedEntry;
    if (entry == null) { PreviewTitle.Text = "Select an item to preview"; return; }

    var source = _settings.HideSourceAppInUi || string.IsNullOrEmpty(entry.SourceApp) ? "" : $" · from {entry.SourceApp}";
    PreviewTitle.Text = $"{entry.CategoryIcon} {entry.Category} · {entry.TypeLabel} · {entry.TimeAgo}"
        + (entry.IsSensitive ? " · 🔒 Sensitive" : "")
        + (entry.IsPinned ? " · Pinned" : "") + (entry.IsFavorite ? " · ★" : "") + source;

    switch (entry.ContentType)
    {
      case ClipboardContentType.Image:
        if (!entry.IsSensitive || !_settings.DisablePreviewForSensitive)
        {
          if (entry.PayloadPath != null && File.Exists(entry.PayloadPath))
          {
            var bytes = _repository.ReadPayload(entry.PayloadPath, _settings.EncryptPayloadFiles);
            if (bytes != null)
            {
              using var ms = new MemoryStream(bytes);
              var bmp = new BitmapImage();
              bmp.BeginInit();
              bmp.CacheOption = BitmapCacheOption.OnLoad;
              bmp.StreamSource = ms;
              bmp.EndInit();
              bmp.Freeze();
              PreviewContentScroll.Visibility = Visibility.Collapsed;
              PreviewImageZoom.Visibility = Visibility.Visible;
              PreviewImageZoom.SetSource(bmp);
              PreviewImageZoom.RefreshLayout();
            }
          }
        }
        break;
      case ClipboardContentType.Files:
        if (!_settings.NeverStoreFilePaths || !entry.IsSensitive)
        {
          PreviewFiles.ItemsSource = entry.FilePaths;
          PreviewFiles.Visibility = Visibility.Visible;
        }
        break;
      default:
        var display = _privacy.GetDisplayText(entry) ?? _privacy.GetDisplayPreview(entry);
        if (IsRichFormattingActive)
        {
          FormattedPreview.Visibility = Visibility.Visible;
          FormattedPreview.ShowEntry(entry, display, _settings, true, this);
        }
        else
        {
          PreviewText.Text = display;
          PreviewText.Visibility = Visibility.Visible;
        }
        break;
    }
  }

  /// <summary>
  /// Copies the clip, hides the manager, and sends Ctrl+V to the app that had focus before.
  /// The manager is for browsing — never the paste target.
  /// </summary>
  private void PasteToActiveApp(ClipboardEntry? entry = null)
  {
    entry ??= SelectedEntry;
    if (entry == null) return;
    using (_capture.SuppressCapture()) _paste.SetClipboard(entry);
    Hide();
    var delay = Math.Max(_settings.PasteDelayMs, 50);
    System.Threading.Tasks.Task.Delay(delay).ContinueWith(_ =>
        Dispatcher.Invoke(ClipboardPasteService.SendPasteKeys));
  }

  private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();
  private void FilterChanged(object sender, RoutedEventArgs e) => RefreshList();
  private void HistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => PasteToActiveApp();
  private void PasteToApp_Click(object sender, RoutedEventArgs e) => PasteToActiveApp();

  private void Copy_Click(object sender, RoutedEventArgs e)
  {
    if (SelectedEntry == null) return;
    using (_capture.SuppressCapture()) _paste.SetClipboard(SelectedEntry);
    StatusText.Text = "● Copied";
  }

  private bool TryCopySelectedImage()
  {
    if (ImageLibraryHost.Visibility == Visibility.Visible && _imageLibraryView.TryCopySelectedImage())
      return true;

    if (HistoryPanel.Visibility == Visibility.Visible
        && PreviewImageZoom.Visibility == Visibility.Visible
        && SelectedEntry?.ContentType == ClipboardContentType.Image)
    {
      Copy_Click(this, new RoutedEventArgs());
      return true;
    }

    return false;
  }

  private void Favorite_Click(object sender, RoutedEventArgs e)
  {
    if (SelectedEntry == null) return;
    _repository.SetFavorite(SelectedEntry.Id, !SelectedEntry.IsFavorite);
    RefreshList();
  }

  private void Pin_Click(object sender, RoutedEventArgs e)
  {
    if (SelectedEntry == null) return;
    _repository.SetPinned(SelectedEntry.Id, !SelectedEntry.IsPinned);
    RefreshList();
  }

  private void Delete_Click(object sender, RoutedEventArgs e)
  {
    if (SelectedEntry == null) return;
    _repository.Delete(SelectedEntry.Id);
    RefreshList();
    UpdatePreview();
  }

  private void ClearClipboard_Click(object sender, RoutedEventArgs e) =>
      _quickActions.ClearSystemClipboard(this);

  private void CopyLatest_Click(object sender, RoutedEventArgs e) =>
      _quickActions.CopyLatestToClipboard(this);

  private void PasteLatest_Click(object sender, RoutedEventArgs e) =>
      _quickActions.PasteLatestToApp(this);

  private void ToggleCapture_Click(object sender, RoutedEventArgs e)
  {
    _quickActions.ToggleCapturePaused(this);
    UpdateCaptureButtonLabel();
  }

  private void ClearUnpinned_Click(object sender, RoutedEventArgs e) =>
      _quickActions.ClearUnpinnedHistory(this);

  private void ClearAppHistory_Click(object sender, RoutedEventArgs e) =>
      _quickActions.ClearAppHistory(this);

  private void ClearAll_Click(object sender, RoutedEventArgs e) =>
      _quickActions.ClearAllHistory(this);

  private void EmptyDatabase_Click(object sender, RoutedEventArgs e) =>
      _quickActions.EmptyDatabase(this);

  private void UpdateCaptureButtonLabel()
  {
    if (PauseCaptureBtn == null) return;
    PauseCaptureBtn.Content = _quickActions.IsCapturePaused ? "Resume capture" : "Pause capture";
  }

  private void ExportDatabase_Click(object sender, RoutedEventArgs e)
  {
    var dlg = new Microsoft.Win32.SaveFileDialog
    {
      Title = "Export CopyPaste Pro database",
      Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
      FileName = $"CopyPastePro-history-{DateTime.Now:yyyyMMdd-HHmmss}.db",
      DefaultExt = ".db",
      AddExtension = true
    };

    if (dlg.ShowDialog(this) != true)
      return;

    try
    {
      _repository.ExportDatabaseTo(dlg.FileName);
      StatusText.Text = "● Database exported";
      AppDialog.Info($"Database exported to:\n{dlg.FileName}", "Export database", this);
    }
    catch (Exception ex)
    {
      AppDialog.Error($"Could not export database:\n{ex.Message}", "Export database", this);
    }
  }

  private void Hide_Click(object sender, RoutedEventArgs e) => Hide();

  protected override void OnActivated(EventArgs e)
  {
    // Hidden manager must not capture activation (breaks quick access popup).
    if (Visibility != Visibility.Visible)
      return;
    base.OnActivated(e);
  }

  private void UpdateFooter()
  {
    var winClip = _settings.WindowsClipboardHistoryDisabledByApp
        ? " · Win+V history: required off"
        : "";
    FooterText.Text =
        $"Sort: {_settings.DefaultSort} · Auto-save: {(_settings.AutoSaveToDatabase ? "on" : "off")} · Backup: {(_settings.AutoBackupEnabled ? $"every {_settings.AutoBackupIntervalMinutes}m" : "off")}{winClip}";
  }

  private bool TryResetVisibleImagePreviewZoom()
  {
    if (HistoryPanel.Visibility == Visibility.Visible
        && PreviewImageZoom.Visibility == Visibility.Visible
        && PreviewImageZoom.Source != null)
    {
      PreviewImageZoom.ResetZoom();
      return true;
    }

    if (ImageLibraryHost.Visibility == Visibility.Visible)
    {
      _imageLibraryView.ResetPreviewZoom();
      return true;
    }

    return false;
  }

  private bool IsTextInputFocused()
  {
    var focused = Keyboard.FocusedElement;
    return focused is System.Windows.Controls.TextBox or System.Windows.Controls.ComboBox;
  }

  private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
  {
    if (Keyboard.Modifiers == ModifierKeys.Control)
    {
      if (e.Key == Key.C && !IsTextInputFocused() && TryCopySelectedImage())
      {
        e.Handled = true;
        return;
      }
      if (e.Key is Key.OemMinus or Key.Subtract)
      {
        AdjustUiScale(-MainWindowUiScale.Step);
        e.Handled = true;
        return;
      }
      if (e.Key is Key.OemPlus or Key.Add)
      {
        AdjustUiScale(MainWindowUiScale.Step);
        e.Handled = true;
        return;
      }
      if (e.Key is Key.D0 or Key.NumPad0)
      {
        if (TryResetVisibleImagePreviewZoom())
        {
          e.Handled = true;
          return;
        }
      }
    }

    if (e.Key == Key.Enter && HistoryPanel.Visibility == Visibility.Visible && !IsTextInputFocused())
    {
      PasteToActiveApp();
      e.Handled = true;
    }
    if (e.Key == Key.Delete && HistoryPanel.Visibility == Visibility.Visible) { Delete_Click(sender, e); e.Handled = true; }
    if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
  }

  private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
  {
    if (Keyboard.Modifiers != ModifierKeys.Control)
      return;
    e.Handled = true;
    AdjustUiScale(MainWindowUiScale.StepFromWheel(e.Delta));
  }

  private void AdjustUiScale(double delta)
  {
    var next = MainWindowUiScale.Clamp(_uiScale + delta);
    if (Math.Abs(next - _uiScale) < 0.001)
      return;
    _uiScale = next;
    ApplyUiScale(save: true);
  }

  private void ApplyUiScale(bool save)
  {
    UiScaleRoot.LayoutTransform = new ScaleTransform(_uiScale, _uiScale);
    if (save)
    {
      _settings.MainWindowUiScale = _uiScale;
      _settings.Save();
    }
  }

  private sealed class HistoryListItem
  {
    public ClipboardEntry Entry { get; }
    public string Icon { get; }
    public string Preview { get; }
    public string Meta { get; }
    public string GroupKey { get; }
    public HistoryListItem(ClipboardEntry e, bool showCat, PrivacyService privacy)
    {
      Entry = e;
      Icon = e.IsSensitive ? "🔒" : (e.IsFavorite ? "★" : e.IsPinned ? "📌" : e.CategoryIcon);
      var cat = showCat ? $"[{e.Category}] " : "";
      Preview = $"{cat}{privacy.GetDisplayPreview(e)}";
      Meta = $"{e.TypeLabel} · {e.TimeAgo}" + (e.SizeBytes > 0 ? $" · {FormatSize(e.SizeBytes)}" : "");
      GroupKey = e.Category;
    }
    private static string FormatSize(long b)
    {
      if (b < 1024) return $"{b} B";
      if (b < 1024 * 1024) return $"{b / 1024.0:F1} KB";
      return $"{b / (1024.0 * 1024):F1} MB";
    }
  }
}
