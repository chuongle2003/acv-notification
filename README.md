# Windows Task Tracker

Ứng dụng WPF theo dõi nhiệm vụ từ một file Excel `.xlsx`, tự refresh khi Excel
được lưu, lưu state trong SQLite và phát Windows app notifications cho nhiệm vụ
sắp đến hạn hoặc quá hạn.

## Yêu cầu

- Phát triển Linux: Docker với .NET 10 SDK image.
- Chạy và phát hành: Windows 11 x64.
- Build installer: Inno Setup 6.7.3.

## Kiểm tra trên Linux

Bật Docker daemon, sau đó chạy:

```bash
./setup_projects.sh
```

Script dùng `compose.yaml` để restore, chạy toàn bộ test cross-platform, rồi
compile code/XAML WPF. Gate Linux tắt PRI generation vì `MakePri.exe` chỉ chạy
trên Windows; self-contained publish được tạo trong Windows gate/CI.

Có thể chạy trực tiếp khi máy đã cài .NET 10 SDK:

```bash
bash eng/verify-linux.sh
```

Linux không thể nghiệm thu render WPF, tray, toast activation, Registry
auto-start hoặc installer. Các phần đó phải qua Windows gate bên dưới.

## Kiểm tra trên Windows

```powershell
pwsh .\eng\verify-windows.ps1
```

Để build luôn installer sau khi đã cài Inno Setup 6:

```powershell
pwsh .\eng\verify-windows.ps1 -BuildInstaller -Version 0.1.0
```

Smoke-test bắt buộc:

1. Chọn file `.xlsx`, sửa và Save bằng Excel; app tự refresh.
2. Manual refresh, rename/delete source và đổi source trong Settings.
3. Correction Keep/Swap/Manual/Unresolved và reset correction.
4. Close-to-tray; menu Mở/Refresh/Pause/Thoát hoạt động.
5. Toast cá nhân/tổng hợp; nút Mở và Đã xem cập nhật đúng row/state.
6. Chưa ack thì nhắc lại, ack dừng Upcoming; Overdue tạo group mới.
7. Single-instance, resume-from-sleep và `--background` hoạt động.
8. Auto-start HKCU được thêm/xóa đúng theo Settings.

## Tạo Setup.exe

Sau khi publish và cài Inno Setup 6.7.3:

```powershell
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' `
  '/DPublishDir=artifacts\publish\win-x64' `
  '/DAppVersion=0.1.0' `
  'installer\TaskTracker.iss'
```

Installer nằm trong `artifacts/installer/`. Installer là per-user, không yêu cầu
admin; upgrade giữ SQLite. Khi uninstall, người dùng được hỏi có xóa dữ liệu
cục bộ hay không.

## CI và release

GitHub Actions chạy Linux core/cross-build và Windows full build/test/publish.
Tag `v0.1.0` mới build thêm `Setup.exe`. Chỉ tạo tag sau khi smoke-test trên
Windows 11 thật không còn lỗi Severity 1/2.

Ứng dụng không sửa file Excel, không telemetry và không gửi dữ liệu ra mạng.
