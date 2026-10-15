@echo off
chcp 65001 >nul
setlocal

echo ========================================================
echo   Сборка программы Strelo — Напоминалка для Windows
echo ========================================================
echo.

set "CSC64=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "CSC32=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
set "CSC="

if exist "%CSC64%" (
    set "CSC=%CSC64%"
) else if exist "%CSC32%" (
    set "CSC=%CSC32%"
)

if defined CSC (
    echo [1/2] Найден встроенный компилятор Windows .NET Framework:
    echo       %CSC%
    echo [2/2] Компиляция StreloReminder.exe...
    "%CSC%" /nologo /target:winexe /optimize+ /utf8output /codepage:65001 /out:StreloReminder.exe ^
        /r:System.dll ^
        /r:System.Core.dll ^
        /r:System.Drawing.dll ^
        /r:System.Windows.Forms.dll ^
        /r:System.Runtime.Serialization.dll ^
        /r:System.Xml.dll ^
        Program.cs ^
        Models\ReminderItem.cs ^
        Models\CloudSyncConfig.cs ^
        Services\StorageService.cs ^
        Services\CloudSyncService.cs ^
        Services\UpdateService.cs ^
        Services\IconGenerator.cs ^
        Forms\PopupNotificationForm.cs ^
        Forms\CloudSyncForm.cs ^
        Forms\MainForm.cs

    if errorlevel 1 (
        echo.
        echo [ОШИБКА] Не удалось скомпилировать проект.
        pause
        exit /b 1
    )

    echo.
    echo [УСПЕШНО] Файл StreloReminder.exe успешно создан!
    echo Запуск программы...
    start "" "%~dp0StreloReminder.exe"
    exit /b 0
)

where dotnet >nul 2>nul
if %errorlevel% equ 0 (
    echo [1/2] Сборка через .NET SDK...
    dotnet build -c Release
    if errorlevel 1 (
        echo [ОШИБКА] Сборка через dotnet build завершилась с ошибкой.
        pause
        exit /b 1
    )
    echo [УСПЕШНО] Проект собран в папке bin\Release\net8.0-windows\
    pause
    exit /b 0
)

echo [ОШИБКА] Не найден ни встроенный компилятор .NET Framework, ни .NET SDK.
pause
exit /b 1
