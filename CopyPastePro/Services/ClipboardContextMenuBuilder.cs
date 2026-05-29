using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfBrush = System.Windows.Media.Brush;

namespace CopyPastePro.Services;

public static class ClipboardContextMenuBuilder
{
  private const string IconPaste = "📋";
  private const string IconCopy = "📄";
  private const string IconSave = "💾";
  private const string IconFolder = "📁";
  private const string IconFile = "📂";
  private const string IconPath = "🔗";
  private const string IconStar = "★";
  private const string IconPin = "📌";
  private const string IconFullscreen = "⛶";
  private const string IconDelete = "🗑";
  private const string IconRefresh = "↻";
  private const string IconManager = "⚙";
  private const string IconSelect = "▤";
  private const string IconClear = "🧹";
  private const string IconPause = "⏸";
  private const string IconPlay = "▶";

  public static ContextMenu Create(Action<ContextMenu> configure)
  {
    var menu = new ContextMenu();
    ApplyStyle(menu);
    configure(menu);
    return menu;
  }

  public static void ApplyStyle(ContextMenu menu)
  {
    if (System.Windows.Application.Current.TryFindResource("AppContextMenu") is Style style)
      menu.Style = style;
    menu.Background = System.Windows.Media.Brushes.Transparent;
    menu.BorderThickness = new Thickness(0);
    menu.Padding = new Thickness(0);
  }

  public static MenuItem Item(string label, Action? click, string? gesture = null, string? icon = null, bool isEnabled = true) =>
      CreateRow(label, click, gesture, icon, isEnabled, danger: false);

  public static MenuItem Danger(string label, Action? click, string? icon = IconDelete, bool isEnabled = true) =>
      CreateRow(label, click, null, icon, isEnabled, danger: true);

  public static MenuItem Section(string title)
  {
    var item = new MenuItem { Header = title.ToUpperInvariant() };
    if (System.Windows.Application.Current.TryFindResource("AppMenuSection") is Style sectionStyle)
      item.Style = sectionStyle;
    return item;
  }

  public static Separator Separator() => new();

  public static void AddClipboardEntryMenu(ContextMenu menu, ClipboardEntryMenuOptions opt)
  {
    var a = opt.Actions;
    menu.Items.Add(Item("Paste to app", a.PasteToApp, "Enter", IconPaste));
    menu.Items.Add(Item("Copy to clipboard", a.Copy, "Ctrl+C", IconCopy));

    if (opt.IsImage && a.Fullscreen != null)
      menu.Items.Add(Item("Fullscreen preview", a.Fullscreen, icon: IconFullscreen));

    if (opt.IsImage && a.ExportImageAs != null)
      menu.Items.Add(Item("Export image as…", a.ExportImageAs, icon: IconSave));

    if (opt.IsImage && a.OpenImageFile != null && opt.HasImageFile)
      menu.Items.Add(Item("Show image in Explorer", a.OpenImageFile, icon: IconFile));

    menu.Items.Add(Separator());
    menu.Items.Add(Section("Organize"));
    menu.Items.Add(Item(opt.IsFavorite ? "Remove from favorites" : "Add to favorites",
        a.ToggleFavorite, icon: IconStar, isEnabled: a.ToggleFavorite != null));
    menu.Items.Add(Item(opt.IsPinned ? "Unpin from top" : "Pin to top",
        a.TogglePin, icon: IconPin, isEnabled: a.TogglePin != null));

    if (opt.IsImage)
      AddImageStorageSections(menu, opt.ImageStorage);

    menu.Items.Add(Separator());
    menu.Items.Add(Section("Remove"));
    menu.Items.Add(Danger("Remove from clipboard history", a.RemoveFromClipboard, isEnabled: a.RemoveFromClipboard != null));

    if (opt.IsImage)
    {
      if (opt.ImageStorage.SyncFileOnDisk && a.DeleteSyncedFile != null)
        menu.Items.Add(Danger("Delete synced file from disk", a.DeleteSyncedFile));
      if (opt.ImageStorage.AutoSaveFileOnDisk && a.DeleteAutoSavedFile != null)
        menu.Items.Add(Danger("Delete auto-saved file from disk", a.DeleteAutoSavedFile));
      if ((opt.ImageStorage.SyncFileOnDisk || opt.ImageStorage.AutoSaveFileOnDisk)
          && a.RemoveFromClipboardAndDeleteFiles != null)
        menu.Items.Add(Danger("Remove from clipboard and delete file(s)", a.RemoveFromClipboardAndDeleteFiles));
    }

    if (opt.ShowOpenManager)
    {
      menu.Items.Add(Separator());
      menu.Items.Add(Item("Open full manager", a.OpenManager, icon: IconManager, isEnabled: a.OpenManager != null));
    }
  }

  public static void AddImageLibraryMenu(ContextMenu menu, ImageLibraryMenuOptions opt)
  {
    var a = opt.Actions;

    menu.Items.Add(Item("Paste to app", a.PasteToApp, "Enter", IconPaste, opt.CanPaste));
    menu.Items.Add(Item("Copy to clipboard", a.Copy, "Ctrl+C", IconCopy));
    if (opt.HasHistoryEntry)
    {
      menu.Items.Add(Item("Save as…", a.SaveAs, icon: IconSave));
      if (a.ExportImageAs != null)
        menu.Items.Add(Item("Export image as…", a.ExportImageAs, icon: IconSave));
    }
    if (opt.OpenFilePath != null)
      menu.Items.Add(Item("Show in File Explorer", a.OpenFile, icon: IconFile));

    menu.Items.Add(Item("Fullscreen preview", a.Fullscreen, icon: IconFullscreen,
        isEnabled: a.Fullscreen != null && opt.HasPreview));

    if (opt.HasHistoryEntry)
    {
      menu.Items.Add(Separator());
      menu.Items.Add(Section("Organize"));
      menu.Items.Add(Item(opt.IsFavorite ? "Remove from favorites" : "Add to favorites",
          a.ToggleFavorite, icon: IconStar));
      menu.Items.Add(Item(opt.IsPinned ? "Unpin from top" : "Pin to top", a.TogglePin, icon: IconPin));
    }

    AddImageStorageSections(menu, opt.ImageStorage);

    menu.Items.Add(Separator());
    menu.Items.Add(Section("Remove"));

    if (opt.HasHistoryEntry)
      menu.Items.Add(Danger("Remove from clipboard history", a.RemoveFromClipboard));

    if (opt.ImageStorage.SyncFileOnDisk && a.DeleteSyncedFile != null)
      menu.Items.Add(Danger("Delete synced file from disk", a.DeleteSyncedFile));

    if (opt.ImageStorage.AutoSaveFileOnDisk && a.DeleteAutoSavedFile != null)
      menu.Items.Add(Danger("Delete auto-saved file from disk", a.DeleteAutoSavedFile));

    if (opt.HasHistoryEntry && (opt.ImageStorage.SyncFileOnDisk || opt.ImageStorage.AutoSaveFileOnDisk)
        && a.RemoveFromClipboardAndDeleteFiles != null)
      menu.Items.Add(Danger("Remove from clipboard and delete file(s)", a.RemoveFromClipboardAndDeleteFiles));

    if (opt.IsFolderOnly && opt.FolderOnlyPath != null)
      menu.Items.Add(Danger("Delete file from disk", a.DeleteFolderOnlyFile));

    if (a.Refresh != null)
    {
      menu.Items.Add(Separator());
      menu.Items.Add(Item("Refresh library", a.Refresh, icon: IconRefresh));
    }
  }

  public static void AddTextPreviewItems(ContextMenu menu, TextPreviewActions actions)
  {
    menu.Items.Add(Item("Copy text", actions.Copy, "Ctrl+C", IconCopy));
    menu.Items.Add(Item("Select all", actions.SelectAll, "Ctrl+A", IconSelect));
  }

  public static void AddQuickClipboardActions(ContextMenu menu, ClipboardQuickActionsMenuOptions opt)
  {
    menu.Items.Add(Separator());
    menu.Items.Add(Section("Clipboard"));
    menu.Items.Add(Item("Clear clipboard", opt.ClearSystemClipboard, icon: IconClear));
    menu.Items.Add(Item("Copy latest to clipboard", opt.CopyLatest, icon: IconCopy));
    if (opt.PasteLatest != null)
      menu.Items.Add(Item("Paste latest to app", opt.PasteLatest, icon: IconPaste));

    menu.Items.Add(Separator());
    menu.Items.Add(Section("CopyPaste Pro history"));
    menu.Items.Add(Item("Clear unpinned from app history", opt.ClearUnpinned, icon: IconClear));
    menu.Items.Add(Danger("Clear all app history", opt.ClearAppHistory));
    menu.Items.Add(Danger("Clear app history + Windows clipboard", opt.ClearAll));

    if (opt.ToggleCapture != null)
    {
      menu.Items.Add(Separator());
      menu.Items.Add(Item(opt.IsCapturePaused ? "Resume capture" : "Pause capture",
          opt.ToggleCapture, icon: opt.IsCapturePaused ? IconPlay : IconPause));
    }

    if (opt.OpenManager != null)
      menu.Items.Add(Item("Open full manager", opt.OpenManager, icon: IconManager));

    if (opt.Refresh != null)
      menu.Items.Add(Item("Refresh", opt.Refresh, icon: IconRefresh));
  }

  public static void AddQuickClipboardActionsOnly(ContextMenu menu, ClipboardQuickActionsMenuOptions opt)
  {
    menu.Items.Add(Section("Clipboard"));
    menu.Items.Add(Item("Clear clipboard", opt.ClearSystemClipboard, icon: IconClear));
    menu.Items.Add(Item("Copy latest to clipboard", opt.CopyLatest, icon: IconCopy));
    if (opt.PasteLatest != null)
      menu.Items.Add(Item("Paste latest to app", opt.PasteLatest, icon: IconPaste));
    menu.Items.Add(Separator());
    menu.Items.Add(Section("CopyPaste Pro history"));
    menu.Items.Add(Item("Clear unpinned from app history", opt.ClearUnpinned, icon: IconClear));
    menu.Items.Add(Danger("Clear all app history", opt.ClearAppHistory));
    menu.Items.Add(Danger("Clear app history + Windows clipboard", opt.ClearAll));
    if (opt.ToggleCapture != null)
    {
      menu.Items.Add(Separator());
      menu.Items.Add(Item(opt.IsCapturePaused ? "Resume capture" : "Pause capture",
          opt.ToggleCapture, icon: opt.IsCapturePaused ? IconPlay : IconPause));
    }
    if (opt.OpenManager != null)
      menu.Items.Add(Item("Open full manager", opt.OpenManager, icon: IconManager));
  }

  public static void AddFullscreenImageMenu(ContextMenu menu, Action? copy, Action close, bool canCopy = true)
  {
    menu.Items.Add(Item("Copy image", copy, "Ctrl+C", IconCopy, canCopy));
    menu.Items.Add(Separator());
    menu.Items.Add(Item("Close", close, "Esc"));
  }

  private static void AddImageStorageSections(ContextMenu menu, ImageStorageMenuInfo storage)
  {
    if (storage.SyncEnabled)
    {
      menu.Items.Add(Separator());
      menu.Items.Add(Section("Sync folder"));
      menu.Items.Add(Item("Open sync folder", storage.OpenSyncFolder, icon: IconFolder,
          isEnabled: storage.OpenSyncFolder != null));
      if (storage.SyncFileOnDisk && storage.ShowSyncedFile != null)
        menu.Items.Add(Item("Show synced file in Explorer", storage.ShowSyncedFile, icon: IconFile));
      if (storage.SyncFileOnDisk && storage.CopySyncPath != null)
        menu.Items.Add(Item("Copy file path", storage.CopySyncPath, icon: IconPath));
    }

    if (storage.AutoSaveEnabled)
    {
      menu.Items.Add(Separator());
      menu.Items.Add(Section("Auto-save"));
      menu.Items.Add(Item("Open auto-save folder", storage.OpenAutoSaveFolder, icon: IconFolder,
          isEnabled: storage.OpenAutoSaveFolder != null));
      if (storage.AutoSaveFileOnDisk && storage.ShowAutoSavedFile != null)
        menu.Items.Add(Item("Show auto-saved file in Explorer", storage.ShowAutoSavedFile, icon: IconFile));
      if (storage.AutoSaveFileOnDisk && storage.CopyAutoSavePath != null)
        menu.Items.Add(Item("Copy auto-save path", storage.CopyAutoSavePath, icon: IconPath));
    }
  }

  public static ImageStorageMenuInfo BuildImageStorageInfo(
      bool syncEnabled,
      bool autoSaveEnabled,
      string? syncFilePath,
      string? autoSaveFilePath,
      ImageStorageMenuActions? actions)
  {
    var syncOnDisk = IsExistingFile(syncFilePath);
    var autoOnDisk = IsExistingFile(autoSaveFilePath);
    return new ImageStorageMenuInfo
    {
      SyncEnabled = syncEnabled,
      AutoSaveEnabled = autoSaveEnabled,
      SyncFileOnDisk = syncOnDisk,
      AutoSaveFileOnDisk = autoOnDisk,
      OpenSyncFolder = actions?.OpenSyncFolder,
      OpenAutoSaveFolder = actions?.OpenAutoSaveFolder,
      ShowSyncedFile = syncOnDisk ? actions?.ShowSyncedFile : null,
      ShowAutoSavedFile = autoOnDisk ? actions?.ShowAutoSavedFile : null,
      CopySyncPath = syncOnDisk ? actions?.CopySyncPath : null,
      CopyAutoSavePath = autoOnDisk ? actions?.CopyAutoSavePath : null
    };
  }

  public static bool IsExistingFile(string? path) =>
      !string.IsNullOrWhiteSpace(path) && File.Exists(path);

  public static void PrepareListBoxRightClick(WpfListBox list, MouseButtonEventArgs e)
  {
    var hit = e.OriginalSource as DependencyObject;
    while (hit != null)
    {
      if (hit is ListBoxItem { DataContext: not null } lbi)
      {
        lbi.IsSelected = true;
        list.SelectedItem = lbi.DataContext;
        return;
      }
      hit = VisualTreeHelper.GetParent(hit);
    }
  }

  public static T? FindParentDataContext<T>(DependencyObject? source) where T : class
  {
    while (source != null)
    {
      if (source is FrameworkElement { Tag: T tag })
        return tag;
      if (source is FrameworkElement { DataContext: T ctx })
        return ctx;
      source = VisualTreeHelper.GetParent(source);
    }
    return null;
  }

  private static MenuItem CreateRow(string label, Action? click, string? gesture, string? icon, bool isEnabled, bool danger)
  {
    var item = new MenuItem
    {
      Header = BuildHeader(icon, label),
      InputGestureText = gesture ?? "",
      IsEnabled = isEnabled && click != null
    };
    if (danger && System.Windows.Application.Current.TryFindResource("AppMenuItemDanger") is Style dangerStyle)
      item.Style = dangerStyle;
    if (click != null)
      item.Click += (_, _) => click();
    return item;
  }

  private static StackPanel BuildHeader(string? icon, string label)
  {
    var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
    row.Children.Add(new TextBlock
    {
      Text = string.IsNullOrEmpty(icon) ? " " : icon,
      Width = 22,
      FontSize = 13,
      VerticalAlignment = VerticalAlignment.Center,
      TextAlignment = TextAlignment.Center
    });
    row.Children.Add(new TextBlock
    {
      Text = label,
      VerticalAlignment = VerticalAlignment.Center,
      Foreground = GetBrush("TextPrimaryBrush")
    });
    return row;
  }

  private static WpfBrush? GetBrush(string key) =>
      System.Windows.Application.Current.TryFindResource(key) as WpfBrush;
}

public sealed class ClipboardEntryMenuOptions
{
  public required ClipboardEntryMenuActions Actions { get; init; }
  public bool IsFavorite { get; init; }
  public bool IsPinned { get; init; }
  public bool IsImage { get; init; }
  public bool HasImageFile { get; init; }
  public bool ShowOpenManager { get; init; }
  public ImageStorageMenuInfo ImageStorage { get; init; } = new();
}

public sealed class ClipboardEntryMenuActions
{
  public Action? PasteToApp { get; init; }
  public Action? Copy { get; init; }
  public Action? Fullscreen { get; init; }
  public Action? ExportImageAs { get; init; }
  public Action? ToggleFavorite { get; init; }
  public Action? TogglePin { get; init; }
  public Action? RemoveFromClipboard { get; init; }
  public Action? DeleteSyncedFile { get; init; }
  public Action? DeleteAutoSavedFile { get; init; }
  public Action? RemoveFromClipboardAndDeleteFiles { get; init; }
  public Action? OpenImageFile { get; init; }
  public Action? OpenManager { get; init; }
}

public sealed class ImageLibraryMenuOptions
{
  public required ImageLibraryMenuActions Actions { get; init; }
  public ImageStorageMenuInfo ImageStorage { get; init; } = new();
  public bool HasHistoryEntry { get; init; }
  public bool IsFolderOnly { get; init; }
  public bool CanPaste { get; init; }
  public bool HasPreview { get; init; } = true;
  public bool IsFavorite { get; init; }
  public bool IsPinned { get; init; }
  public string? OpenFilePath { get; init; }
  public string? FolderOnlyPath { get; init; }
}

public sealed class ImageLibraryMenuActions
{
  public Action? PasteToApp { get; init; }
  public Action? Copy { get; init; }
  public Action? SaveAs { get; init; }
  public Action? ExportImageAs { get; init; }
  public Action? OpenFile { get; init; }
  public Action? Fullscreen { get; init; }
  public Action? ToggleFavorite { get; init; }
  public Action? TogglePin { get; init; }
  public Action? RemoveFromClipboard { get; init; }
  public Action? DeleteSyncedFile { get; init; }
  public Action? DeleteAutoSavedFile { get; init; }
  public Action? RemoveFromClipboardAndDeleteFiles { get; init; }
  public Action? DeleteFolderOnlyFile { get; init; }
  public Action? Refresh { get; init; }
}

public sealed class ImageStorageMenuInfo
{
  public bool SyncEnabled { get; init; }
  public bool AutoSaveEnabled { get; init; }
  public bool SyncFileOnDisk { get; init; }
  public bool AutoSaveFileOnDisk { get; init; }
  public Action? OpenSyncFolder { get; init; }
  public Action? OpenAutoSaveFolder { get; init; }
  public Action? ShowSyncedFile { get; init; }
  public Action? ShowAutoSavedFile { get; init; }
  public Action? CopySyncPath { get; init; }
  public Action? CopyAutoSavePath { get; init; }
}

public sealed class ImageStorageMenuActions
{
  public Action? OpenSyncFolder { get; init; }
  public Action? OpenAutoSaveFolder { get; init; }
  public Action? ShowSyncedFile { get; init; }
  public Action? ShowAutoSavedFile { get; init; }
  public Action? CopySyncPath { get; init; }
  public Action? CopyAutoSavePath { get; init; }
}

public sealed class TextPreviewActions
{
  public Action? Copy { get; init; }
  public Action? SelectAll { get; init; }
}

public sealed class ClipboardQuickActionsMenuOptions
{
  public Action? ClearSystemClipboard { get; init; }
  public Action? CopyLatest { get; init; }
  public Action? PasteLatest { get; init; }
  public Action? ClearUnpinned { get; init; }
  public Action? ClearAppHistory { get; init; }
  public Action? ClearAll { get; init; }
  public Action? ToggleCapture { get; init; }
  public bool IsCapturePaused { get; init; }
  public Action? OpenManager { get; init; }
  public Action? Refresh { get; init; }
}
