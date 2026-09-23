param([string]$Run='r2',[string]$ProductDll,[string]$ProbeDll)
$ErrorActionPreference='Stop'
if($Run -notmatch '^r[0-9]+$'){throw 'Invalid run id'}
$root='C:/Users/choco/Documents/Codex/2026-09-13/new-chat-2/work/mapgenai-headless-runtime'
$repo='C:/Users/choco/Documents/Codex/2026-09-13/new-chat-2/work/mapgenai-guided/repo'
if(-not $ProductDll){$ProductDll=Join-Path $repo 'dev/Assemblies/MapGenAI.dll'}
if(-not $ProbeDll){$ProbeDll='C:/Users/choco/Documents/Codex/2026-09-13/new-chat-2/work/mapgenai-guided/native-feedback/bin/Debug/net472/MapGenAI.FeedbackProbe.dll'}
$profile=Join-Path $root ('profile-'+$Run)
$output=Join-Path $repo ('docs/analysis/2026-09-23-recommendation-feedback/verification/native-headless-'+$Run)
$mod=Join-Path $root 'Mods/FeedbackProbe'
if(-not (Test-Path -LiteralPath (Join-Path $root 'MAPGENAI_HEADLESS_OWNED'))){throw 'Ownership marker required'}
if(Test-Path -LiteralPath $profile){throw 'Fresh profile required'}
New-Item -ItemType Directory -Path (Join-Path $profile 'Config'),$output,(Join-Path $mod 'About'),(Join-Path $mod 'Assemblies') -Force | Out-Null
[xml]$about=Get-Content -LiteralPath (Join-Path $repo 'dev/About/About.xml') -Raw
$about.ModMetaData.packageId='choco.mapgenai.feedbackprobe'
$about.ModMetaData.name='MapGenAI local headless verification'
$about.Save((Join-Path $mod 'About/About.xml'))
Copy-Item -LiteralPath $ProductDll -Destination (Join-Path $mod 'Assemblies/MapGenAI.dll')
Copy-Item -LiteralPath $ProbeDll -Destination (Join-Path $mod 'Assemblies/MapGenAI.FeedbackProbe.dll')
Copy-Item -LiteralPath (Join-Path $repo 'dev/Languages') -Destination $mod -Recurse -Force
$active=@('brrainz.harmony','ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey','m00nl1ght.mappreview','choco.mapgenai.feedbackprobe')
$version=(Get-Content -LiteralPath (Join-Path $root 'Version.txt') -Raw).Trim()
$config='<ModsConfigData><version>'+$version+'</version><activeMods>'+ (($active|ForEach-Object {'<li>'+$_+'</li>'}) -join '') + '</activeMods></ModsConfigData>'
[IO.File]::WriteAllText((Join-Path $profile 'Config/ModsConfig.xml'),$config)
[IO.File]::WriteAllText((Join-Path $profile 'Config/Prefs.xml'),'<Prefs><langFolderName>Korean (한국어)</langFolderName><runInBackground>true</runInBackground></Prefs>')
[IO.File]::WriteAllText((Join-Path $profile 'MAPGENAI_DISPOSABLE'),'Owned headless verification, no user saves.')
$arguments=@('-batchmode','-nographics',('-savedatafolder="'+$profile+'"'),('-mapgenAIProbe="'+$output+'"'),'-logFile',('"'+(Join-Path $output 'Player.log')+'"'))
$process=Start-Process -FilePath (Join-Path $root 'RimWorldWin64.exe') -WorkingDirectory $root -ArgumentList $arguments -WindowStyle Hidden -PassThru
@{pid=$process.Id;root=$root;profile=$profile;output=$output;sourceDllSha256=(Get-FileHash -LiteralPath $ProductDll).Hash;started=(Get-Date).ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'launch.json') -Encoding utf8
Write-Output ('Headless owned process '+$process.Id)
