param([string]$EncoderPath)
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
Push-Location -LiteralPath $projectRoot
try {
    [xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'LightCapture.csproj') -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid project version.' }
    $sourceRevision = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'A committed Git checkout is required.' }
    if (@(git status --porcelain).Count) { throw 'Commit source changes before packaging a release.' }
    if ([string]::IsNullOrWhiteSpace($EncoderPath)) { $EncoderPath = Join-Path $projectRoot 'tools\ffmpeg.exe' }
    $encoderFile = (Resolve-Path -LiteralPath $EncoderPath -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath $encoderFile -PathType Leaf)) { throw 'Supply FFmpeg for the synthetic encoder tests.' }
    $testEncoderHash = (Get-FileHash -LiteralPath $encoderFile -Algorithm SHA256).Hash
    $workRoot = Join-Path $projectRoot ('dist\release-' + $version + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
    $packageName = "LightCapture-$version-win-x64"
    $packageRoot = Join-Path $workRoot $packageName
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    & dotnet publish (Join-Path $projectRoot 'LightCapture.csproj') -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -p:IncludeFfmpeg=false -p:DebugType=None -p:DebugSymbols=false -o $packageRoot --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $executable = Join-Path $packageRoot 'LightCapture.exe'
    $proofRoot = Join-Path $workRoot 'verification'
    New-Item -ItemType Directory -Path $proofRoot -Force | Out-Null
    $coreReportPath = Join-Path $proofRoot 'program-results.json'
    $testArtifacts = Join-Path $proofRoot 'test-artifacts'
    $toolbarRoot = Join-Path $proofRoot 'toolbar'
    $appearanceRoot = Join-Path $proofRoot 'appearance'
    # The encoder is only a test fixture; the published application uses an external encoder.
    $testEncoderPath = Join-Path $packageRoot 'tools\ffmpeg.exe'
    if (Test-Path -LiteralPath $testEncoderPath) { throw 'Unexpected encoder in the public payload.' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $testEncoderPath) -Force | Out-Null
    Copy-Item -LiteralPath $encoderFile -Destination $testEncoderPath
    try {
        $coreRun = Start-Process -FilePath $executable -ArgumentList "--self-test --report `"$coreReportPath`" --artifacts `"$testArtifacts`"" -WindowStyle Hidden -Wait -PassThru
        if ($coreRun.ExitCode -ne 0) { throw "Program checks failed. Review $coreReportPath" }
    }
    finally {
        # This is one known file inside the newly-created package directory.
        if (Test-Path -LiteralPath $testEncoderPath) { Remove-Item -LiteralPath $testEncoderPath -Force }
    }
    $toolbarRun = Start-Process -FilePath $executable -ArgumentList "--toolbar-proof --artifacts `"$toolbarRoot`"" -WindowStyle Hidden -Wait -PassThru
    if ($toolbarRun.ExitCode -ne 0) { throw 'Toolbar checks failed.' }
    $appearanceRun = Start-Process -FilePath $executable -ArgumentList "--appearance-proof --artifacts `"$appearanceRoot`"" -WindowStyle Hidden -Wait -PassThru
    if ($appearanceRun.ExitCode -ne 0) { throw 'Interface checks failed.' }
    $core = Get-Content -LiteralPath $coreReportPath -Raw | ConvertFrom-Json
    $toolbar = Get-Content -LiteralPath (Join-Path $toolbarRoot 'results.json') -Raw | ConvertFrom-Json
    $appearance = Get-Content -LiteralPath (Join-Path $appearanceRoot 'results.json') -Raw | ConvertFrom-Json
    foreach ($document in @('README.md','README.en.md','CHANGELOG.md','CONTRIBUTING.md','IMPLEMENTATION.md','THIRD-PARTY-NOTICES.md','LICENSE')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination $packageRoot
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'tools\README.md') -Destination (Join-Path $packageRoot 'tools\README.md')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $packageRoot -Recurse
    Copy-Item -LiteralPath $coreReportPath -Destination (Join-Path $packageRoot 'program-verification.json')
    Copy-Item -LiteralPath (Join-Path $toolbarRoot 'results.json') -Destination (Join-Path $packageRoot 'toolbar-verification.json')
    Copy-Item -LiteralPath (Join-Path $appearanceRoot 'results.json') -Destination (Join-Path $packageRoot 'interface-verification.json')
    $quickStart = @'
# 轻截便携版

完整解压文件夹后运行 LightCapture.exe，无需另外安装.NET。

- 默认 Ctrl+Alt+A 开始截图；左键托盘图标也可截图，右键打开功能菜单。
- 截图时右键取消当前操作，程序继续在系统托盘中运行。
- 翻译服务在“偏好设置 → 翻译后端”配置；图片不上传，仅发送文字。
- 录屏与GIF导出需要FFmpeg。本包不包含编码器，请在“偏好设置 → 录屏编码器”选择本机ffmpeg.exe，或放到程序的tools目录。
- 更新前先在旧版托盘菜单中退出，再启动新版。

此版本为试用版。跨应用粘贴、多显示器、混合DPI和真实音频设备仍需实机验证。程序检查中的视频／音频输入使用合成内容，翻译接口使用模拟HTTP响应。

功能和环境要求见 [README](README.md)，第三方许可见 [第三方声明](THIRD-PARTY-NOTICES.md)。
'@
    [System.IO.File]::WriteAllText((Join-Path $packageRoot '使用说明.md'), $quickStart, [System.Text.UTF8Encoding]::new($false))
    $privateFiles = @(Get-ChildItem -LiteralPath $packageRoot -File -Recurse | Where-Object { $_.Name -in @('ffmpeg.exe','settings.json','error.log') -or $_.Extension -eq '.pdb' })
    if ($privateFiles.Count) { throw 'Unexpected fixture, private data or debug symbols in the package.' }
    $files = @(Get-ChildItem -LiteralPath $packageRoot -File -Recurse | ForEach-Object {
        [pscustomobject]@{ path = [System.IO.Path]::GetRelativePath($packageRoot, $_.FullName).Replace('\','/'); size = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    [pscustomobject]@{
        version = $version; sourceRevision = $sourceRevision; builtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        platform = 'win-x64'; selfContained = $true; ffmpegBundled = $false
        encoderTestsUsedExternalFixture = $true; testEncoderSha256 = $testEncoderHash
        programTestsPassed = $core.passed; toolbarLayoutChecksPassed = $toolbar.passed; interfaceChecksPassed = $appearance.passed
        guiTests = 'pending-user-trial'; files = $files
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $packageRoot 'manifest.json') -Encoding utf8
    $zipPath = Join-Path $workRoot "LightCapture-$version-win-x64-portable.zip"
    Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal
    $sourceZip = Join-Path $workRoot "LightCapture-$version-source.zip"
    git archive --format=zip "--prefix=LightCapture-$version/" "--output=$sourceZip" $sourceRevision
    if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
    $checksumPath = Join-Path $workRoot 'SHA256SUMS.txt'
    $checksums = @($zipPath, $sourceZip) | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [System.IO.Path]::GetFileName($_) }
    [System.IO.File]::WriteAllLines($checksumPath, $checksums, [System.Text.UTF8Encoding]::new($false))
    $result = [pscustomobject]@{
        version = $version; sourceRevision = $sourceRevision; packageDirectory = $packageRoot
        portableZip = $zipPath; sourceZip = $sourceZip; checksums = $checksumPath
        bytes = (Get-Item -LiteralPath $zipPath).Length; programChecks = $core.passed; toolbarChecks = $toolbar.passed; interfaceChecks = $appearance.passed
        ffmpegBundled = $false
    }
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot "dist\release-$version.json") -Encoding utf8
    $result | ConvertTo-Json
}
finally { Pop-Location }
