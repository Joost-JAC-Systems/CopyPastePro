using System.Windows;
using CopyPastePro.Views;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace CopyPastePro.Services;

/// <summary>App-themed dialogs replacing system <see cref="System.Windows.MessageBox"/>.</summary>
public static class AppDialog
{
  public static MessageBoxResult Show(
      string message,
      string title = "CopyPaste Pro",
      MessageBoxButton buttons = MessageBoxButton.OK,
      MessageBoxImage icon = MessageBoxImage.None,
      Window? owner = null)
  {
    var dlg = new AppDialogWindow(message, title, buttons, icon);
    ConfigureOwner(dlg, owner);
    dlg.ShowDialog();
    return dlg.Result;
  }

  public static void Info(string message, string title = "CopyPaste Pro", Window? owner = null) =>
      Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information, owner);

  public static void Warning(string message, string title = "CopyPaste Pro", Window? owner = null) =>
      Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning, owner);

  public static void Error(string message, string title = "CopyPaste Pro", Window? owner = null) =>
      Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error, owner);

  public static bool Confirm(string message, string title = "CopyPaste Pro", Window? owner = null) =>
      Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, owner) == MessageBoxResult.Yes;

  public static bool ConfirmWarning(string message, string title = "CopyPaste Pro", Window? owner = null) =>
      Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, owner) == MessageBoxResult.Yes;

  /// <summary>Prompt for a blocked private/incognito copy. Returns true if the user chose allow only this time.</summary>
  public static bool ConfirmIncognitoCapture(string windowTitle, string contentTypeLabel, Window? owner = null)
  {
    var message =
        "This copy is from a private or incognito browser window (InPrivate, Incognito, etc.).\n\n"
        + $"Window: {windowTitle}\n"
        + $"Content: {contentTypeLabel}\n\n"
        + "Save it to your clipboard history?";
    var dlg = new AppDialogWindow(message, "Private browsing", MessageBoxImage.Question, "Don't save", "Allow only this time");
    ConfigureOwner(dlg, owner);
    dlg.ShowDialog();
    return dlg.Result == MessageBoxResult.Yes;
  }

  private static void ConfigureOwner(Window dlg, Window? owner)
  {
    var host = owner ?? GetActiveAppWindow();
    if (host == null || !host.IsLoaded)
    {
      dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
      return;
    }

    dlg.Owner = host;
    dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
  }

  private static Window? GetActiveAppWindow()
  {
    if (System.Windows.Application.Current?.MainWindow is { IsVisible: true } main)
      return main;

    return System.Windows.Application.Current?.Windows
        .OfType<Window>()
        .FirstOrDefault(w => w.IsActive && w.IsVisible);
  }
}
