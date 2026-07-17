@echo off
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /out:EarPicker.exe  EarPicker.cs
if errorlevel 1 exit /b %errorlevel%
echo Built EarPickerControl.exe
