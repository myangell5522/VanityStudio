@echo off
rem Double-click to upload workshop_preview.gif as the Workshop cover. Extra arguments are passed through,
rem e.g.  "Publish Workshop Preview.bat" -PublishedFileId 1234567890
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish_preview.ps1" %*
echo.
pause
