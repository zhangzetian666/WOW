@echo off
if exist "%~dp0bin\WoWProbe.exe" (
  start "" "%~dp0bin\WoWProbe.exe" quick-start
) else (
  start "" "%~dp0WoWProbe.exe" quick-start
)
