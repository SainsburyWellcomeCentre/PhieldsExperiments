<#
This script performs the following actions:
1. Cleans the .bonsai directory by removing untracked files and directories.
2. Checks the existence of Bonsai.exe in the current directory.
3. If Bonsai.exe is not found, it retrieves the latest release of Bonsai from GitHub.
4. It reads the Bonsai.config file to determine the dependencies of Bonsai to download.
6. Executes Bonsai.exe with the --no-editor option.
7. Run dotnet bonsai.sgen to generate custom classes using specified schemas using the specified namespaces.
8. Returns to the initial directory.
#>

git clean -fdx bonsai
Set-Location -Path .\bonsai
# Start-Process -FilePath .\setup.cmd -Wait

if (!(Test-Path "./Bonsai.exe")) {
    $release = "https://github.com/bonsai-rx/bonsai/releases/latest/download/Bonsai.zip"
    $configPath = "./Bonsai.config"
    if (Test-Path $configPath) {
        [xml]$config = Get-Content $configPath
        $bootstrapper = $config.PackageConfiguration.Packages.Package.where{$_.id -eq 'Bonsai'}
        if ($bootstrapper) {
            $version = $bootstrapper.version
            $release = "https://github.com/bonsai-rx/bonsai/releases/download/$version/Bonsai.zip"
        }
    }
    Invoke-WebRequest $release -OutFile "temp.zip"
    Move-Item -Path "NuGet.config" "temp.config" -ErrorAction SilentlyContinue
    Expand-Archive "temp.zip" -DestinationPath ".\" -Force
    Move-Item -Path "temp.config" "NuGet.config" -Force -ErrorAction SilentlyContinue
    Remove-Item -Path "temp.zip" -ErrorAction SilentlyContinue
    Remove-Item -Path "Bonsai32.exe" -ErrorAction SilentlyContinue
}
& ./Bonsai.exe --no-editor

git clean -fdx .venv
git clean -fx uv.lock
Set-Location -Path .\
irm https://astral.sh/uv/install.ps1 | iex
uv sync --all-extras