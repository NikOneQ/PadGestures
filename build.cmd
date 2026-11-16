@echo off
rem Build PadGestures.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist assets\app.ico powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-icon.ps1
if not exist dist mkdir dist
"%FW%\csc.exe" /nologo /target:winexe /codepage:65001 /optimize+ /out:dist\PadGestures.exe /win32icon:assets\app.ico ^
  /r:"%FW%\WPF\PresentationFramework.dll" /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\WindowsBase.dll" ^
  /r:"%FW%\System.Xaml.dll" /r:"%FW%\WPF\UIAutomationClient.dll" /r:"%FW%\WPF\UIAutomationTypes.dll" ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll ^
  /resource:src\Ui.xaml,Ui.xaml src\PadGestures.cs
