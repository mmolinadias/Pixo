# Gera o instalador do Pixo (Velopack) na pasta "releases".
#
#   .\release.ps1                 -> só gera (Setup.exe + pacotes) para testar
#   .\release.ps1 -Upload         -> gera e publica como Release no GitHub (precisa de $env:GITHUB_TOKEN)
#
# Normalmente quem publica é o GitHub Actions (.github/workflows/release.yml), ao criar uma tag "v1.2.3".
# A versão vem do <Version> do Pixo\Pixo.csproj.
param([switch]$Upload)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$repo = 'https://github.com/mmolinadias/Pixo'
[xml]$proj = Get-Content 'Pixo\Pixo.csproj'
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'Não achei o <Version> no Pixo.csproj' }
Write-Host "Pixo $version" -ForegroundColor Cyan

# vpk (ferramenta do Velopack) na mesma versão do pacote Velopack do app
$vpkVersion = '1.2.161'
$env:PATH += ";$env:USERPROFILE\.dotnet\tools"
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) { dotnet tool install -g vpk --version $vpkVersion }

# 1) publica com o .NET embutido (o outro PC não precisa instalar nada antes)
Remove-Item publish\app -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish Pixo\Pixo.csproj -c Release -r win-x64 --self-contained true -o publish\app
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falhou' }

# 2) baixa a release anterior (para gerar o pacote "delta", bem menor, para quem já tem o Pixo)
New-Item -ItemType Directory -Force releases | Out-Null
try { vpk download github --repoUrl $repo -o releases } catch { Write-Host 'Sem release anterior (ok na primeira vez).' }

# 3) empacota: Setup.exe + pacote completo + delta
vpk pack --packId Pixo --packVersion $version --packDir publish\app --mainExe Pixo.exe --runtime win-x64 `
    --packTitle Pixo --packAuthors Pixo --icon Pixo\Assets\pixo.ico `
    --shortcuts 'Desktop,StartMenuRoot' -o releases
if ($LASTEXITCODE -ne 0) { throw 'vpk pack falhou' }

# 4) publica no GitHub (opcional)
if ($Upload) {
    if (-not $env:GITHUB_TOKEN) { throw 'Defina $env:GITHUB_TOKEN para publicar.' }
    vpk upload github --repoUrl $repo --token $env:GITHUB_TOKEN -o releases `
        --publish --releaseName "Pixo $version" --tag "v$version"
}

Write-Host "Pronto: releases\Pixo-win-Setup.exe" -ForegroundColor Green
