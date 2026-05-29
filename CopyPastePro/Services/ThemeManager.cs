using System.Windows;

namespace CopyPastePro.Services;

public static class ThemeManager
{
  public static void Apply(AppSettings settings) => ApplyTheme(
      settings.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark");

  public static void ApplyTheme(string theme)
  {
    if (System.Windows.Application.Current is not { } app) return;

    var merged = app.Resources.MergedDictionaries;
    for (var i = merged.Count - 1; i >= 0; i--)
    {
      var src = merged[i].Source?.OriginalString ?? "";
      if (src.Contains("Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
          src.Contains("Light.xaml", StringComparison.OrdinalIgnoreCase) ||
          src.Contains("Colors.xaml", StringComparison.OrdinalIgnoreCase))
        merged.RemoveAt(i);
    }

    merged.Insert(0, new ResourceDictionary
    {
      Source = new Uri($"Themes/{theme}.xaml", UriKind.Relative)
    });
  }
}
