using System.Windows;
using CopyPastePro.Services;

namespace CopyPastePro.Views;

public partial class PrivacyPinDialog : Window
{
  private readonly PrivacyService _privacy;

  private PrivacyPinDialog(PrivacyService privacy)
  {
    _privacy = privacy;
    InitializeComponent();
    PinBox.Focus();
  }

  public static bool TryUnlock(PrivacyService privacy, AppSettings settings)
  {
    if (string.IsNullOrEmpty(settings.PrivacyLockPin)) return true;
    var dlg = new PrivacyPinDialog(privacy);
    return dlg.ShowDialog() == true;
  }

  private void Unlock_Click(object sender, RoutedEventArgs e)
  {
    if (_privacy.TryUnlock(PinBox.Password))
    {
      _privacy.SetPrivacyLock(false);
      DialogResult = true;
      Close();
      return;
    }
    AppDialog.Warning("Incorrect PIN.", "Privacy lock", this);
    PinBox.Clear();
    PinBox.Focus();
  }
}
