@echo off
pushd %~dp0
powershell -ExecutionPolicy Bypass -File ./setup.ps1
popd