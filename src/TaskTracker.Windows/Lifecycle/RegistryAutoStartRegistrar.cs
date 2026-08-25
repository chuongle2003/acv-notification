using System;
using System.Reflection;
using Microsoft.Win32;
using TaskTracker.Application;

namespace TaskTracker.Windows.Lifecycle;

/// <summary>
/// Per-user auto-start via HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// No admin rights required; toggle off removes the value.
/// </summary>
public class RegistryAutoStartRegistrar : IAutoStartRegistrar
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TaskTracker";

    public void Enable(string executablePath, string[] args)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Executable path is required", nameof(executablePath));
        }

        var command = args.Length > 0
            ? $"\"{executablePath}\" {string.Join(" ", args)}"
            : $"\"{executablePath}\"";

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException($"Cannot open registry key HKCU\\{RunKeyPath}");
        key.SetValue(ValueName, command);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        // Missing key/value is already the desired state — nothing to do.
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) != null;
    }
}
