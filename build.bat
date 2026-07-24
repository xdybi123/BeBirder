@echo off
setlocal

set "SRC=%~dp0"
set "OUTPUT=%~dp0EarPicker.exe"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if exist "%CSC%" goto compiler_found
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if exist "%CSC%" goto compiler_found

>&2 echo Error: The .NET Framework C# compiler was not found under "%WINDIR%\Microsoft.NET".
exit /b 1

:compiler_found
"%CSC%" /nologo /target:winexe /optimize+ /out:"%OUTPUT%" ^
    "%SRC%Program.cs" ^
    "%SRC%DeviceProtocol.cs" ^
    "%SRC%DeviceClient.cs" ^
    "%SRC%JpegUtil.cs" ^
    "%SRC%JpegFragmentReassembler.cs" ^
    "%SRC%VideoSession.cs" ^
    "%SRC%SensorSession.cs" ^
    "%SRC%MjpegPublisher.cs" ^
    "%SRC%RotatingView.cs" ^
    "%SRC%NativeMethods.cs" ^
    "%SRC%MainForm.cs" ^
    "%SRC%MainForm.Layout.cs" ^
    "%SRC%MainForm.Device.cs" ^
    "%SRC%MainForm.Video.cs" ^
    "%SRC%MainForm.Sensor.cs" ^
    "%SRC%MainForm.LiveStatus.cs" ^
    "%SRC%MainForm.Capture.cs" ^
    "%SRC%MainForm.Publish.cs"
set "BUILD_EXIT=%ERRORLEVEL%"
if not "%BUILD_EXIT%"=="0" exit /b %BUILD_EXIT%

echo Built "%OUTPUT%"
exit /b 0
