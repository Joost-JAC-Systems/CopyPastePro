using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CopyPastePro.Services;
using MessageBoxResult = System.Windows.MessageBoxResult;
using WpfButton = System.Windows.Controls.Button;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace CopyPastePro.Views;

public partial class AppDialogWindow : Window
{
  private MessageBoxResult _result = MessageBoxResult.None;
  private WpfButton? _defaultButton;

  public MessageBoxResult Result => _result;

  public AppDialogWindow(string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
      : this(message, title, icon, null, null)
  {
  }

  /// <param name="customCancel">Left/ghost button label (e.g. Don't save).</param>
  /// <param name="customConfirm">Right/primary button label (e.g. Allow only this time).</param>
  public AppDialogWindow(string message, string title, MessageBoxImage icon, string? customCancel, string? customConfirm)
  {
    InitializeComponent();
    MouseLeftButtonDown += (_, e) =>
    {
      if (e.ButtonState == MouseButtonState.Pressed)
        DragMove();
    };
    TitleText.Text = string.IsNullOrWhiteSpace(title) ? "CopyPaste Pro" : title;
    MessageText.Text = message;
    ApplyIcon(icon);
    if (customCancel != null && customConfirm != null)
      BuildCustomButtons(customCancel, customConfirm);
    else
      BuildButtons(MessageBoxButton.OK);
    Loaded += (_, _) => _defaultButton?.Focus();
  }

  private void ApplyIcon(MessageBoxImage icon)
  {
    if (icon is MessageBoxImage.Information or MessageBoxImage.None)
    {
      IconImage.Visibility = Visibility.Visible;
      IconGlyph.Visibility = Visibility.Collapsed;
      IconBadge.Background = System.Windows.Media.Brushes.Transparent;
      try { IconImage.Source = AppIconSource.Transparent; } catch { }
      return;
    }

    IconImage.Visibility = Visibility.Collapsed;
    IconGlyph.Visibility = Visibility.Visible;

    string glyph;
    System.Windows.Media.Brush badgeBg;
    System.Windows.Media.Brush glyphFg;

    switch (icon)
    {
      case MessageBoxImage.Warning:
        glyph = "⚠";
        badgeBg = new SolidColorBrush(System.Windows.Media.Color.FromArgb(48, 255, 107, 107));
        glyphFg = (System.Windows.Media.Brush)FindResource("DangerBrush");
        break;
      case MessageBoxImage.Error:
        glyph = "✕";
        badgeBg = new SolidColorBrush(System.Windows.Media.Color.FromArgb(56, 255, 80, 80));
        glyphFg = (System.Windows.Media.Brush)FindResource("DangerBrush");
        break;
      case MessageBoxImage.Question:
        glyph = "?";
        badgeBg = new SolidColorBrush(System.Windows.Media.Color.FromArgb(48, 108, 92, 231));
        glyphFg = (System.Windows.Media.Brush)FindResource("AccentLightBrush");
        break;
      default:
        glyph = "ℹ";
        badgeBg = new SolidColorBrush(System.Windows.Media.Color.FromArgb(48, 0, 184, 148));
        glyphFg = (System.Windows.Media.Brush)FindResource("SuccessBrush");
        break;
    }

    IconGlyph.Text = glyph;
    IconGlyph.Foreground = glyphFg;
    IconBadge.Background = badgeBg;
  }

  private void BuildCustomButtons(string cancelLabel, string confirmLabel)
  {
    void AddButton(string label, MessageBoxResult result, bool isDefault, bool isCancel, bool primary)
    {
      var btn = new WpfButton
      {
        Content = label,
        Padding = new Thickness(16, 8, 16, 8),
        Margin = new Thickness(8, 0, 0, 0),
        MinWidth = 88,
        IsDefault = isDefault,
        IsCancel = isCancel,
        Style = (Style)FindResource(primary ? "PrimaryButton" : "GhostButton")
      };
      btn.Click += (_, _) =>
      {
        _result = result;
        DialogResult = result is MessageBoxResult.OK or MessageBoxResult.Yes;
        Close();
      };
      if (isDefault) _defaultButton = btn;
      ButtonPanel.Children.Add(btn);
    }

    AddButton(cancelLabel, MessageBoxResult.No, isDefault: false, isCancel: true, primary: false);
    AddButton(confirmLabel, MessageBoxResult.Yes, isDefault: true, isCancel: false, primary: true);
  }

  private void BuildButtons(MessageBoxButton buttons)
  {
  void AddButton(string label, MessageBoxResult result, bool isDefault, bool isCancel, bool primary)
    {
      var btn = new WpfButton
      {
        Content = label,
        Padding = new Thickness(16, 8, 16, 8),
        Margin = new Thickness(8, 0, 0, 0),
        MinWidth = 88,
        IsDefault = isDefault,
        IsCancel = isCancel,
        Style = (Style)FindResource(primary ? "PrimaryButton" : "GhostButton")
      };
      btn.Click += (_, _) =>
      {
        _result = result;
        DialogResult = result is MessageBoxResult.OK or MessageBoxResult.Yes;
        Close();
      };
      if (isDefault) _defaultButton = btn;
      ButtonPanel.Children.Add(btn);
    }

    switch (buttons)
    {
      case MessageBoxButton.OKCancel:
        AddButton("Cancel", MessageBoxResult.Cancel, isDefault: false, isCancel: true, primary: false);
        AddButton("OK", MessageBoxResult.OK, isDefault: true, isCancel: false, primary: true);
        break;
      case MessageBoxButton.YesNoCancel:
        AddButton("Cancel", MessageBoxResult.Cancel, isDefault: false, isCancel: true, primary: false);
        AddButton("No", MessageBoxResult.No, isDefault: false, isCancel: false, primary: false);
        AddButton("Yes", MessageBoxResult.Yes, isDefault: true, isCancel: false, primary: true);
        break;
      case MessageBoxButton.YesNo:
        AddButton("No", MessageBoxResult.No, isDefault: false, isCancel: true, primary: false);
        AddButton("Yes", MessageBoxResult.Yes, isDefault: true, isCancel: false, primary: true);
        break;
      default:
        AddButton("OK", MessageBoxResult.OK, isDefault: true, isCancel: true, primary: true);
        break;
    }
  }

  private void Window_KeyDown(object sender, WpfKeyEventArgs e)
  {
    if (e.Key != Key.Escape) return;
    _result = MessageBoxResult.Cancel;
    DialogResult = false;
    Close();
    e.Handled = true;
  }
}
