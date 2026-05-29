using System.Text.RegularExpressions;
using CopyPastePro.Models;

namespace CopyPastePro.Services;

public sealed class SensitiveDetectionResult
{
  public bool IsSensitive { get; init; }
  public string Reason { get; init; } = "";
  public SensitiveDataKind Kind { get; init; } = SensitiveDataKind.None;
}

public enum SensitiveDataKind
{
  None,
  CreditCard,
  SocialSecurityNumber,
  Iban,
  Email,
  Phone,
  ApiKey,
  Jwt,
  PrivateKey,
  PasswordLike,
  CryptoWallet,
  CustomPattern,
  BlockedDomain,
  Keyword
}

public sealed class SensitiveDataDetector
{
  private static readonly Regex CreditCard = new(@"\b(?:\d[ -]*?){13,19}\b", RegexOptions.Compiled);
  private static readonly Regex Ssn = new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled);
  private static readonly Regex Iban = new(@"\b[A-Z]{2}\d{2}[A-Z0-9]{11,30}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
  private static readonly Regex Email = new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled);
  private static readonly Regex Phone = new(@"\b(?:\+?\d{1,3}[-.\s]?)?\(?\d{2,4}\)?[-.\s]?\d{3,4}[-.\s]?\d{3,4}\b", RegexOptions.Compiled);
  private static readonly Regex ApiKey = new(@"\b(?:sk|pk|rk|api|token|secret|key)[-_]?[A-Za-z0-9]{16,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
  private static readonly Regex Jwt = new(@"\beyJ[A-Za-z0-9_-]+\.eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b", RegexOptions.Compiled);
  private static readonly Regex PrivateKey = new(@"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----", RegexOptions.Compiled | RegexOptions.IgnoreCase);
  private static readonly Regex PasswordField = new(@"(?i)(password|passwd|pwd|secret|pin\s*code)\s*[:=]\s*\S+", RegexOptions.Compiled);
  private static readonly Regex CryptoBtc = new(@"\b[13][a-km-zA-HJ-NP-Z1-9]{25,34}\b", RegexOptions.Compiled);
  private static readonly Regex CryptoEth = new(@"\b0x[a-fA-F0-9]{40}\b", RegexOptions.Compiled);

  public SensitiveDetectionResult Analyze(string? text, AppSettings settings)
  {
    if (string.IsNullOrWhiteSpace(text))
      return new SensitiveDetectionResult();

    if (settings.BlockCreditCards && CreditCard.IsMatch(text) && PassesLuhn(text))
      return Hit(SensitiveDataKind.CreditCard, "Credit card number");

    if (settings.BlockSocialSecurityNumbers && Ssn.IsMatch(text))
      return Hit(SensitiveDataKind.SocialSecurityNumber, "Social Security Number");

    if (settings.BlockIbanAndBankNumbers && Iban.IsMatch(text))
      return Hit(SensitiveDataKind.Iban, "IBAN / bank account");

    if (settings.BlockEmailAddresses && Email.IsMatch(text))
      return Hit(SensitiveDataKind.Email, "Email address");

    if (settings.BlockPhoneNumbers && Phone.IsMatch(text))
      return Hit(SensitiveDataKind.Phone, "Phone number");

    if (settings.BlockApiKeysAndTokens && ApiKey.IsMatch(text))
      return Hit(SensitiveDataKind.ApiKey, "API key or token");

    if (settings.BlockJwtTokens && Jwt.IsMatch(text))
      return Hit(SensitiveDataKind.Jwt, "JWT token");

    if (settings.BlockPrivateKeys && PrivateKey.IsMatch(text))
      return Hit(SensitiveDataKind.PrivateKey, "Private key");

    if (settings.BlockPasswordLikeContent && PasswordField.IsMatch(text))
      return Hit(SensitiveDataKind.PasswordLike, "Password-like content");

    if (settings.BlockCryptoWalletAddresses && (CryptoBtc.IsMatch(text) || CryptoEth.IsMatch(text)))
      return Hit(SensitiveDataKind.CryptoWallet, "Cryptocurrency address");

    foreach (var domain in settings.BlockedDomains)
    {
      if (string.IsNullOrWhiteSpace(domain)) continue;
      if (text.Contains(domain.Trim(), StringComparison.OrdinalIgnoreCase))
        return Hit(SensitiveDataKind.BlockedDomain, $"Blocked domain: {domain.Trim()}");
    }

    if (!string.IsNullOrWhiteSpace(settings.CustomBlockedRegex))
    {
      try
      {
        if (Regex.IsMatch(text, settings.CustomBlockedRegex, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)))
          return Hit(SensitiveDataKind.CustomPattern, "Custom blocked pattern");
      }
      catch { }
    }

    if (settings.AutoDeleteSensitivePatterns || settings.BlockSensitiveKeywords)
    {
      foreach (var part in settings.SensitivePatterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      {
        if (text.Contains(part, StringComparison.OrdinalIgnoreCase))
          return Hit(SensitiveDataKind.Keyword, $"Sensitive keyword: {part}");
      }
    }

    return new SensitiveDetectionResult();
  }

  public static string Redact(string text, SensitiveDataKind kind)
  {
    if (string.IsNullOrEmpty(text)) return text;
    return kind switch
    {
      SensitiveDataKind.CreditCard => CreditCard.Replace(text, "•••• •••• •••• ••••"),
      SensitiveDataKind.SocialSecurityNumber => Ssn.Replace(text, "***-**-****"),
      SensitiveDataKind.Email => Email.Replace(text, m => MaskKeepEnds(m.Value, 2, 1)),
      SensitiveDataKind.Phone => Phone.Replace(text, "••• ••• ••••"),
      SensitiveDataKind.ApiKey or SensitiveDataKind.Jwt => "[REDACTED TOKEN]",
      SensitiveDataKind.PrivateKey => "[REDACTED PRIVATE KEY]",
      SensitiveDataKind.PasswordLike => PasswordField.Replace(text, "$1 [REDACTED]"),
      _ => "[REDACTED SENSITIVE DATA]"
    };
  }

  private static SensitiveDetectionResult Hit(SensitiveDataKind kind, string reason) =>
      new() { IsSensitive = true, Kind = kind, Reason = reason };

  private static string MaskKeepEnds(string value, int start, int end)
  {
    if (value.Length <= start + end) return new string('•', value.Length);
    return value[..start] + new string('•', Math.Min(value.Length - start - end, 12)) + value[^end..];
  }

  private static bool PassesLuhn(string text)
  {
    var digits = new List<int>();
    foreach (var c in text)
      if (char.IsDigit(c)) digits.Add(c - '0');
    if (digits.Count < 13 || digits.Count > 19) return false;
    var sum = 0;
    var alt = false;
    for (var i = digits.Count - 1; i >= 0; i--)
    {
      var n = digits[i];
      if (alt) { n *= 2; if (n > 9) n -= 9; }
      sum += n;
      alt = !alt;
    }
    return sum % 10 == 0;
  }
}
