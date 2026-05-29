using System.Runtime.InteropServices;
using System.Windows;
using Windows.ApplicationModel.DataTransfer;
using WinClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;
using WpfClipboard = System.Windows.Clipboard;
using WpfDataObject = System.Windows.DataObject;

namespace CopyPastePro.Services;

/// <summary>Clears the active Windows clipboard and Win+V clipboard history.</summary>
public static class SystemClipboardHelper
{
  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool OpenClipboard(IntPtr hWndNewOwner);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool CloseClipboard();

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool EmptyClipboard();

  public sealed class ClearResult
  {
    public bool ActiveClipboardCleared { get; set; }
    public bool HistoryCleared { get; set; }
    public int HistoryItemsRemoved { get; set; }
    public string? Error { get; set; }
    public bool Success => ActiveClipboardCleared && HistoryCleared;
  }

  /// <summary>Clears active clipboard (Ctrl+V) and Windows clipboard history (Win+V), including pinned items.</summary>
  public static ClearResult TryClearWindowsFully(int attempts = 12)
  {
    var result = new ClearResult();
    try
    {
      result.HistoryItemsRemoved = TryClearWindowsHistoryItems();
      result.HistoryCleared = TryClearWindowsHistoryApi();
    }
    catch (Exception ex)
    {
      result.Error = ex.Message;
    }

    for (var i = 0; i < attempts; i++)
    {
      if (TryClearActiveClipboardOnce())
      {
        result.ActiveClipboardCleared = true;
        break;
      }
      Thread.Sleep(30 + i * 20);
    }

    if (!result.ActiveClipboardCleared)
      result.Error = "Could not open or empty the Windows clipboard.";

    if (!result.HistoryCleared)
      result.Error = (result.Error ?? "") + " Win+V history may still contain items (enable Clipboard history in Windows Settings).";

    return result;
  }

  public static bool TrySetDataObject(WpfDataObject data, int attempts = 12)
  {
    for (var i = 0; i < attempts; i++)
    {
      try
      {
        WpfClipboard.SetDataObject(data, true);
        try { WpfClipboard.Flush(); } catch { }
        return true;
      }
      catch (COMException) when (i < attempts - 1)
      {
        Thread.Sleep(25 + i * 15);
      }
      catch (ExternalException) when (i < attempts - 1)
      {
        Thread.Sleep(25 + i * 15);
      }
    }
    return false;
  }

  private static bool TryClearWindowsHistoryApi()
  {
    try
    {
      if (WinClipboard.IsHistoryEnabled())
        return WinClipboard.ClearHistory();
      return true;
    }
    catch
    {
      return false;
    }
  }

  /// <summary>ClearHistory() does not remove pinned Win+V items — delete each entry explicitly.</summary>
  private static int TryClearWindowsHistoryItems()
  {
    try
    {
      if (!WinClipboard.IsHistoryEnabled())
        return 0;

      var op = WinClipboard.GetHistoryItemsAsync();
      var itemsResult = op.AsTask().GetAwaiter().GetResult();
      if (itemsResult.Status != ClipboardHistoryItemsResultStatus.Success)
        return 0;

      var removed = 0;
      foreach (var item in itemsResult.Items)
      {
        try
        {
          if (WinClipboard.DeleteItemFromHistory(item))
            removed++;
        }
        catch { }
      }

      WinClipboard.ClearHistory();
      return removed;
    }
    catch
    {
      return 0;
    }
  }

  private static bool TryClearActiveClipboardOnce()
  {
    if (TryNativeEmpty() && IsActiveClipboardEmpty())
      return true;

    try
    {
      WpfClipboard.SetDataObject(new WpfDataObject(), true);
      try { WpfClipboard.Flush(); } catch { }
      if (IsActiveClipboardEmpty()) return true;
    }
    catch { }

    try
    {
      WpfClipboard.Clear();
      try { WpfClipboard.Flush(); } catch { }
      return IsActiveClipboardEmpty();
    }
    catch
    {
      return false;
    }
  }

  private static bool IsActiveClipboardEmpty()
  {
    try
    {
      var data = WpfClipboard.GetDataObject();
      if (data == null) return true;
      var formats = data.GetFormats(autoConvert: false);
      return formats == null || formats.Length == 0;
    }
    catch
    {
      return false;
    }
  }

  private static bool TryNativeEmpty()
  {
    if (!OpenClipboard(IntPtr.Zero))
      return false;
    try
    {
      return EmptyClipboard();
    }
    finally
    {
      CloseClipboard();
    }
  }
}
