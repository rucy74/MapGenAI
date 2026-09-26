param([string]$Run='n1',[string]$ProductDll,[string]$ProbeDll,[ValidateSet("open_basin","winding_valley","foothills","followup","composition")][string]$Set="open_basin",[switch]$Graphics,[ValidateSet("classic","organic")][string]$Layout="classic",[string]$Evidence="2026-09-23-natural-landforms",[string]$StatesDirectory,[string]$FullState)
$ErrorActionPreference='Stop'
if($Run -notmatch '^n[0-9]+$'){throw 'Invalid run id'}
if($Evidence -notmatch '^20[0-9]{2}-[0-9]{2}-[0-9]{2}-[a-z-]+$'){throw 'Invalid evidence folder'}
$root='C:/Users/choco/Documents/Codex/2026-09-13/new-chat-2/work/mapgenai-headless-runtime'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
if(-not $ProductDll){$ProductDll=Join-Path $repo 'dev/Assemblies/MapGenAI.dll'}
if(-not $ProbeDll){$ProbeDll=Join-Path $PSScriptRoot 'bin/Debug/net472/MapGenAI.NaturalProbe.dll'}
$probeProfile=Join-Path $root ('natural-profile-'+$Run)
$output=Join-Path $repo ('docs/analysis/'+$Evidence+'/native-'+$Run)
$mod=Join-Path $root ('Mods/NaturalProbe-'+$Run)
$packageId='choco.mapgenai.naturalprobe.'+$Run
if(-not (Test-Path -LiteralPath (Join-Path $root 'MAPGENAI_HEADLESS_OWNED'))){throw 'Ownership marker required'}
if(Test-Path -LiteralPath $probeProfile){throw 'Fresh profile required'}
foreach($path in @($output,$mod)){if(Test-Path -LiteralPath $path){throw 'Fresh output and probe paths required'}}
New-Item -ItemType Directory -Path (Join-Path $probeProfile 'Config'),$output,(Join-Path $mod 'About'),(Join-Path $mod 'Assemblies') -Force | Out-Null
[xml]$about=Get-Content -LiteralPath (Join-Path $repo 'dev/About/About.xml') -Raw
$about.ModMetaData.packageId=$packageId
$about.ModMetaData.name='MapGenAI local headless verification'
$about.Save((Join-Path $mod 'About/About.xml'))
Copy-Item -LiteralPath $ProductDll -Destination (Join-Path $mod 'Assemblies/MapGenAI.dll')
Copy-Item -LiteralPath $ProbeDll -Destination (Join-Path $mod 'Assemblies/MapGenAI.FeedbackProbe.dll')
Copy-Item -LiteralPath (Join-Path $repo 'dev/Languages') -Destination $mod -Recurse -Force
$active=@('brrainz.harmony','ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey','m00nl1ght.mappreview',$packageId)
$version=(Get-Content -LiteralPath (Join-Path $root 'Version.txt') -Raw).Trim()
$config='<ModsConfigData><version>'+$version+'</version><activeMods>'+ (($active|ForEach-Object {'<li>'+$_+'</li>'}) -join '') + '</activeMods></ModsConfigData>'
[IO.File]::WriteAllText((Join-Path $probeProfile 'Config/ModsConfig.xml'),$config)
[IO.File]::WriteAllText((Join-Path $probeProfile 'Config/Prefs.xml'),'<Prefs><langFolderName>Korean (한국어)</langFolderName><runInBackground>true</runInBackground></Prefs>')
[IO.File]::WriteAllText((Join-Path $probeProfile 'MAPGENAI_DISPOSABLE'),'Owned headless verification, no user saves.')
$arguments=@(('-mapgenAIProbeSet='+$Set),('-mapgenAIProbeLayout='+$Layout),'-batchmode','-nographics',('-savedatafolder="'+$probeProfile+'"'),('-mapgenAIProbe="'+$output+'"'),'-logFile',('"'+(Join-Path $output 'Player.log')+'"'))
if($Graphics){$arguments=@($arguments | Where-Object {$_ -ne '-nographics'})+@('-force-d3d11')}
if($StatesDirectory){$arguments+=('-mapgenAIProbeStates="'+(Resolve-Path -LiteralPath $StatesDirectory).Path+'"')}
if($FullState){$arguments+=('-mapgenAIProbeFullState="'+(Resolve-Path -LiteralPath $FullState).Path+'"')}
$process=Start-Process -FilePath (Join-Path $root 'RimWorldWin64.exe') -WorkingDirectory $root -ArgumentList $arguments -WindowStyle Hidden -PassThru
@{pid=$process.Id;root=$root;profile=$probeProfile;output=$output;sourceDllSha256=(Get-FileHash -LiteralPath $ProductDll).Hash;started=(Get-Date).ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'launch.json') -Encoding utf8
Write-Output ('Headless owned process '+$process.Id)
