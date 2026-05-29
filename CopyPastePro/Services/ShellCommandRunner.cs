using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using CopyPastePro.Views;

namespace CopyPastePro.Services;

public enum ShellKind { Cmd, PowerShell, Bash }

public static class ShellCommandRunner
{
  public static bool LooksLikeCommand(string text, string ext)
  {
    ext = ext.ToLowerInvariant();
    if (ext is ".ps1" or ".psm1" or ".bat" or ".cmd" or ".sh" or ".bash") return true;
    var t = text.Trim();
    if (t.Length > 4000 || t.Split('\n').Length > 40) return false;
    if (t.StartsWith("#!/")) return true;
    if (Regex.IsMatch(t, @"^(Get-|Set-|New-|Remove-|Start-|Stop-|Invoke-|Write-|cd\s|dir\s|ls\s|npm\s|git\s|docker\s|dotnet\s|pip\s|cargo\s|kubectl\s|winget\s|choco\s)", RegexOptions.IgnoreCase | RegexOptions.Multiline))
      return true;
    if (Regex.IsMatch(t, @"^(@echo|echo\s|set\s|if\s|for\s|call\s|powershell\s|pwsh\s|wsl\s|bash\s|sudo\s)", RegexOptions.IgnoreCase | RegexOptions.Multiline))
      return true;
    return false;
  }

  public static ShellKind DetectShell(string text, string ext)
  {
    ext = ext.ToLowerInvariant();
    if (ext is ".ps1" or ".psm1" || text.Contains("Get-") || text.Contains("Write-Host"))
      return ShellKind.PowerShell;
    if (ext is ".sh" or ".bash" || text.TrimStart().StartsWith("#!/bin/bash") || text.TrimStart().StartsWith("#!/usr/bin/env bash"))
      return ShellKind.Bash;
    return ShellKind.Cmd;
  }

  public static bool CanRunOnThisMachine(ShellKind kind) => kind switch
  {
    ShellKind.PowerShell => true,
    ShellKind.Cmd => true,
    ShellKind.Bash => File.Exists(@"C:\Windows\System32\bash.exe") || File.Exists(@"C:\Windows\System32\wsl.exe"),
    _ => false
  };

  public static void Run(string command, ShellKind kind, Window? owner, bool confirm)
  {
    var trimmed = command.Trim();
    if (string.IsNullOrEmpty(trimmed)) return;

    if (confirm && !AppDialog.Confirm(
            $"Run this command in {kind}?\n\n{Truncate(trimmed, 500)}",
            "Run command", owner))
      return;

    try
    {
      var psi = kind switch
      {
        ShellKind.PowerShell => new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command {Quote(trimmed)}")
        {
          UseShellExecute = true,
          CreateNoWindow = false
        },
        ShellKind.Bash => new ProcessStartInfo(
            File.Exists(@"C:\Windows\System32\wsl.exe") ? "wsl.exe" : "bash.exe",
            File.Exists(@"C:\Windows\System32\wsl.exe") ? Quote(trimmed) : $"-lc {Quote(trimmed)}")
        {
          UseShellExecute = true,
          CreateNoWindow = false
        },
        _ => new ProcessStartInfo("cmd.exe", $"/c {trimmed}") { UseShellExecute = true, CreateNoWindow = false }
      };
      Process.Start(psi);
    }
    catch (Exception ex)
    {
      AppDialog.Error($"Could not run command:\n{ex.Message}", "Run command", owner);
    }
  }

  private static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";

  private static string Truncate(string s, int max) =>
      s.Length <= max ? s : s[..max] + "…";
}
