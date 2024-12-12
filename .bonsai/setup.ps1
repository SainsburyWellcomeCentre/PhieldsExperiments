if (!(Test-Path "./Bonsai.exe")) {
    Invoke-WebRequest "https://github.com/bonsai-rx/bonsai/releases/download/2.9.0-preview1/Bonsai.zip" -OutFile "temp.zip"
    Expand-Archive "temp.zip" -DestinationPath "." -Force
    Remove-Item -Path "temp.zip"
    Remove-Item -Path "Bonsai32.exe"
}
& .\Bonsai.exe --no-editor