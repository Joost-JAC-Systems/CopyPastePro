using System.Security.Cryptography;
using System.Text;

namespace CopyPastePro.Services;

public static class DataProtectionHelper
{
  private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CopyPastePro.v1");

  public static string? ProtectString(string? plain)
  {
    if (string.IsNullOrEmpty(plain)) return plain;
    var bytes = Encoding.UTF8.GetBytes(plain);
    var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
    return "DPAPI:" + Convert.ToBase64String(protectedBytes);
  }

  public static string? UnprotectString(string? stored)
  {
    if (string.IsNullOrEmpty(stored)) return stored;
    if (!stored.StartsWith("DPAPI:", StringComparison.Ordinal)) return stored;
    try
    {
      var protectedBytes = Convert.FromBase64String(stored[6..]);
      var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
      return Encoding.UTF8.GetString(bytes);
    }
    catch
    {
      return "[encrypted]";
    }
  }

  public static byte[] ProtectBytes(byte[] plain)
  {
    return ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
  }

  public static byte[] UnprotectBytes(byte[] protectedBytes) =>
      ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
}
