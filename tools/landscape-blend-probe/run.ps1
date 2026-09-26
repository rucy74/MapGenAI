param(
  [Parameter(Mandatory=$true)][ValidatePattern('^blend-[a-z0-9-]+$')][string]$Run,
  [Parameter(Mandatory=$true)][string]$ProductDll,
  [ValidateSet('A','B','C','D','protected','road')][string]$Case='A',
  [ValidateSet('null','none','natural')][string]$Details='natural',
  [string]$ProbeDll,[switch]$Graphics,[switch]$QuietStructures
)
$ErrorActionPreference='Stop'
$root='C:/Users/choco/Documents/Codex/2026-09-13/new-chat-2/work/mapgenai-headless-runtime'
$repo='C:/Users/choco/Documents/Codex/2026-09-13/new-chat-2/work/mapgenai-guided/repo'
if(-not $ProbeDll){$ProbeDll=Join-Path $PSScriptRoot 'bin/Debug/net472/MapGenAI.LandscapeBlendProbe.dll'}
$ProductDll=(Resolve-Path -LiteralPath $ProductDll).Path
$ProbeDll=(Resolve-Path -LiteralPath $ProbeDll).Path
if(-not(Test-Path -LiteralPath (Join-Path $root 'MAPGENAI_HEADLESS_OWNED'))){throw 'Owned game marker required'}
$profile=Join-Path $root ('landscape-profile-'+$Run)
$output=Join-Path $repo ('docs/analysis/2026-09-23-landscape-blending/native-'+$Run)
$mod=Join-Path $root ('Mods/LandscapeBlendProbe-'+$Run)
foreach($path in @($profile,$output,$mod)){if(Test-Path -LiteralPath $path){throw ('Fresh path required: '+$path)}}
# These are additions inside the marked owned copy only; no installed game or user saves are modified.
New-Item -ItemType Directory -Path (Join-Path $profile 'Config'),$output,(Join-Path $mod 'About'),(Join-Path $mod 'Assemblies') -Force | Out-Null
[xml]$about=Get-Content -LiteralPath (Join-Path $repo 'dev/About/About.xml') -Raw
$packageId='choco.mapgenai.landscapeblendprobe.'+$Run
$about.ModMetaData.packageId=$packageId
$about.ModMetaData.name='MapGenAI disposable landscape blend audit'
$about.Save((Join-Path $mod 'About/About.xml'))
Copy-Item -LiteralPath $ProductDll -Destination (Join-Path $mod 'Assemblies/MapGenAI.dll')
Copy-Item -LiteralPath $ProbeDll -Destination (Join-Path $mod 'Assemblies/MapGenAI.LandscapeBlendProbe.dll')
Copy-Item -LiteralPath (Join-Path $repo 'dev/Languages') -Destination $mod -Recurse
$active=@('brrainz.harmony','ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey','m00nl1ght.mappreview',$packageId)
$version=(Get-Content -LiteralPath (Join-Path $root 'Version.txt') -Raw).Trim()
$config='<ModsConfigData><version>'+$version+'</version><activeMods>'+(($active|ForEach-Object {'<li>'+$_+'</li>'}) -join '')+'</activeMods></ModsConfigData>'
[IO.File]::WriteAllText((Join-Path $profile 'Config/ModsConfig.xml'),$config)
[IO.File]::WriteAllText((Join-Path $profile 'Config/Prefs.xml'),'<Prefs><langFolderName>Korean (한국어)</langFolderName><runInBackground>true</runInBackground></Prefs>')
[IO.File]::WriteAllText((Join-Path $profile 'MAPGENAI_DISPOSABLE'),'Owned disposable landscape audit; no user saves or provider settings.')
$arguments=@(('-mapgenAIBlendCase='+$Case),('-mapgenAIBlendDetails='+$Details),'-batchmode','-nographics',('-savedatafolder="'+$profile+'"'),('-mapgenAIBlendProbe="'+$output+'"'),'-logFile',('"'+(Join-Path $output 'Player.log')+'"'))
if($QuietStructures){$arguments+=@('-mapgenAIBlendQuietStructures=true')}
if($Graphics){$arguments=@($arguments|Where-Object {$_ -ne '-nographics'})+@('-force-d3d11')}
$process=Start-Process -FilePath (Join-Path $root 'RimWorldWin64.exe') -WorkingDirectory $root -ArgumentList $arguments -WindowStyle Hidden -PassThru
@{pid=$process.Id;root=$root;profile=$profile;output=$output;case=$Case;details=$Details;quietStructures=[bool]$QuietStructures;sourceDll=$ProductDll;sourceDllSha256=(Get-FileHash -LiteralPath $ProductDll).Hash;probeDllSha256=(Get-FileHash -LiteralPath $ProbeDll).Hash;started=(Get-Date).ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'launch.json') -Encoding utf8
Write-Output ('Owned process '+$process.Id+'; result: '+$output)
