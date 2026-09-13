param(
    [string]$GameRoot='G:/SteamLibrary/steamapps/common/RimWorld',
    [string]$Output='',
    [string]$Profile='',
    [string]$ProbeMod='',
    [switch]$Render,
    [switch]$Settings,
    [switch]$NaturalShapes,
    [switch]$FeatureRemoval,
    [string]$SourceDll='',
    [string]$ModelConfig='',
    [string]$Language='',
    [string]$ImageInputs='',
    [string]$ImageStates=''
)
$ErrorActionPreference='Stop'
$probeStamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$probeRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $Output) { $Output=Join-Path $probeRepo "docs/analysis/2026-09-13-implementation/runtime-$probeStamp" }
if (-not $Profile) { $Profile=Join-Path $env:LOCALAPPDATA "mapgen-ai-probe/profile-$probeStamp" }
if (-not $ProbeMod) { $ProbeMod=Join-Path $GameRoot "Mods/MapGenAI_Probe_$probeStamp" }
$probeOutput=[IO.Path]::GetFullPath($Output)
$probeProfile=[IO.Path]::GetFullPath($Profile)
$probeModPath=[IO.Path]::GetFullPath($ProbeMod)
if (Test-Path -LiteralPath $probeProfile) {throw 'Use a fresh isolated profile.'}
if (Test-Path -LiteralPath $probeModPath) {throw 'Use a fresh probe mod folder.'}
$gameExe=Join-Path $GameRoot 'RimWorldWin64.exe'
$mainDll=if($SourceDll){[IO.Path]::GetFullPath($SourceDll)}else{Join-Path $probeRepo 'dev/Assemblies/MapGenAI.dll'}
$probeDll=Join-Path $PSScriptRoot 'bin/Debug/net472/MapGenAI.RuntimeProbe.dll'
foreach($required in @($gameExe,$mainDll,$probeDll)) {if(-not (Test-Path -LiteralPath $required)) {throw "Missing required file: $required"}}
New-Item -ItemType Directory -Path $probeOutput,(Join-Path $probeProfile 'Config'),(Join-Path $probeModPath 'About'),(Join-Path $probeModPath 'Assemblies') -Force | Out-Null
$probePackage='choco.mapgenai.probe.'+$probeStamp.Replace('-','')
[xml]$about=Get-Content -LiteralPath (Join-Path $probeRepo 'dev/About/About.xml') -Raw
$about.ModMetaData.packageId=$probePackage
$about.ModMetaData.name='MapGenAI isolated development probe'
$about.Save((Join-Path $probeModPath 'About/About.xml'))
foreach($folder in @('Languages','Textures','Defs','Patches')) {
    $source=Join-Path $probeRepo "dev/$folder"
    if(Test-Path -LiteralPath $source) {Copy-Item -LiteralPath $source -Destination $probeModPath -Recurse}
}
Copy-Item -LiteralPath $mainDll,$probeDll -Destination (Join-Path $probeModPath 'Assemblies')
$known=@()
foreach($dlc in Get-ChildItem -LiteralPath (Join-Path $GameRoot 'Data') -Directory) {
    $dlcAbout=Join-Path $dlc.FullName 'About/About.xml'
    if(Test-Path -LiteralPath $dlcAbout) {[xml]$metadata=Get-Content -LiteralPath $dlcAbout -Raw; $known+=$metadata.ModMetaData.packageId.ToLowerInvariant()}
}
$expansionOrder=@('ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey')
$active=@('brrainz.harmony')+@($expansionOrder | Where-Object { $known -contains $_ })+@('m00nl1ght.mappreview',$probePackage)
$version=(Get-Content -LiteralPath (Join-Path $GameRoot 'Version.txt') -Raw).Trim()
$config='<?xml version="1.0" encoding="utf-8"?><ModsConfigData><version>'+$version+'</version><activeMods>'+ (($active|ForEach-Object {'<li>'+$_+'</li>'}) -join '') + '</activeMods><knownExpansions>'+ (($known|ForEach-Object {'<li>'+$_+'</li>'}) -join '') +'</knownExpansions></ModsConfigData>'
[IO.File]::WriteAllText((Join-Path $probeProfile 'Config/ModsConfig.xml'),$config,[Text.UTF8Encoding]::new($false))
if($Language){
    if($Language -notin @('English','Korean','Japanese','ChineseSimplified')){throw 'Unsupported probe language'}
    $probeLanguageName=if($Language -eq 'Korean'){'Korean (한국어)'}else{$Language}
    [IO.File]::WriteAllText((Join-Path $probeProfile 'Config/Prefs.xml'),('<Prefs><langFolderName>'+$probeLanguageName+'</langFolderName><screenWidth>1280</screenWidth><screenHeight>800</screenHeight><fullscreen>false</fullscreen></Prefs>'),[Text.UTF8Encoding]::new($false))
}
[IO.File]::WriteAllText((Join-Path $probeProfile 'MAPGENAI_DISPOSABLE'),'new test world only; never load a user save')
[IO.File]::WriteAllText((Join-Path $probeModPath 'MAPGENAI_PROBE_OWNED'),$probeProfile)
$arguments=@('-screen-fullscreen','0','-screen-width','960','-screen-height','640',('-savedatafolder="'+$probeProfile+'"'),('-mapgenAIProbe="'+$probeOutput+'"'),'-logFile',('"'+(Join-Path $probeOutput 'Player.log')+'"'))
if(-not $Render){$arguments=@('-batchmode')+$arguments}
if($Render){$arguments+='-mapgenAIProbeRender=true'}
if($Settings){$arguments+='-mapgenAISettingsProbe=true'}
if($NaturalShapes){$arguments+='-mapgenAINaturalShapes=true'}
if($FeatureRemoval){$arguments+='-mapgenAIFeatureRemoval=true'}
if($ModelConfig){$arguments+=('-mapgenAIModelConfig="'+[IO.Path]::GetFullPath($ModelConfig)+'"')}
if($ImageInputs){$arguments+=('-mapgenAIImageInputs="'+[IO.Path]::GetFullPath($ImageInputs)+'"')}
if($ImageStates){$arguments+=('-mapgenAIImageStates="'+[IO.Path]::GetFullPath($ImageStates)+'"')}
$process=Start-Process -FilePath $gameExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
$manifest=@{pid=$process.Id;profile=$probeProfile;mod=$probeModPath;output=$probeOutput;sourceDllSha256=(Get-FileHash -LiteralPath $mainDll -Algorithm SHA256).Hash;probeDllSha256=(Get-FileHash -LiteralPath $probeDll -Algorithm SHA256).Hash;created=(Get-Date).ToString('o')}
$manifest|ConvertTo-Json | Set-Content -LiteralPath (Join-Path $probeOutput 'launch.json') -Encoding utf8
$cleanupArgs=@('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',('"'+(Join-Path $PSScriptRoot 'cleanup.ps1')+'"'),'-Manifest',('"'+(Join-Path $probeOutput 'launch.json')+'"'),'-GameRoot',('"'+$GameRoot+'"'),'-WaitForExit')
Start-Process -FilePath 'powershell.exe' -ArgumentList $cleanupArgs -WindowStyle Hidden | Out-Null
$manifest|ConvertTo-Json
