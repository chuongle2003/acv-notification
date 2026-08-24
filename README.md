# Windows Task Tracker

A desktop application to track document tasks from an Excel file.

## Requirements

- .NET 10 SDK
- Windows 11 (for full functionality)
- Linux (supported for core development and cross-building)

## Building

### On Linux

You can build the core components and run cross-platform tests on Linux:

```bash
dotnet build
dotnet test
```

### On Windows

To build the full solution including the WPF application and run all tests:

```cmd
dotnet build
dotnet test
```
