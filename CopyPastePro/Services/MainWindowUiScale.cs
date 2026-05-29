namespace CopyPastePro.Services;

public static class MainWindowUiScale
{
  public const double Min = 0.75;
  public const double Max = 1.5;
  public const double Step = 0.05;

  public static double Clamp(double scale) => Math.Clamp(scale, Min, Max);

  public static double StepFromWheel(int delta) => delta > 0 ? Step : -Step;
}
