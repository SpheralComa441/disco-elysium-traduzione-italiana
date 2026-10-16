@echo off
rem Disco Elysium - traduzione italiana.
rem Avvia Installa.ps1, che compila i sorgenti della cartella "sorgenti" e apre la finestra di installazione.
title Disco Elysium - Traduzione italiana
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Installa.ps1" %*
set rc=%errorlevel%
if not "%rc%"=="0" pause
exit /b %rc%
