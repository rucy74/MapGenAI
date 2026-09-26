param(
  [Parameter(Mandatory=$true)][ValidatePattern('^native-[a-z0-9-]+$')][string]$Run,
  [Parameter(Mandatory=$true)][string]$ProductDll,
  [ValidateSet('pool','hotspring','protected','exact','native-ground','native-feature')][string]$Case='pool',
  [ValidateSet('temperate','desert','cold','boreal','arid')][string]$Biome='temperate',
  [ValidateSet('none','natural')][string]$Details='natural',
  [ValidateSet('single','cardinal')][string]$InteractionLayout='single',
  [ValidatePattern('^[0-9]{4}-[0-9]{2}-[0-9]{2}-[a-z0-9-]+$')][string]$AnalysisGroup='2026-09-26-shoreline-review',
  [string]$ProbeDll,[string]$StateFile,[string]$WaterProfile,[string]$WaterFill='WaterShallow',[ValidateSet('inland','river')][string]$TileContext='inland',[switch]$FieldChecks,[switch]$IntegrationChecks,[switch]$BypassBlend,[switch]$Graphics
)
$ErrorActionPreference='Stop'
$runtimeRoot='C:/Users/choco/Documents/Codex/2026-09-13/new-chat-2/work/mapgenai-headless-runtime'
$repoRoot='F:/Projects/Rimworld/active/mapgen_ai'
if(-not $ProbeDll){
  $ProbeDll=Join-Path $PSScriptRoot 'bin/Debug/net472/MapGenAI.NativeVisualProbe.dll'
  if(-not(Test-Path -LiteralPath $ProbeDll)){throw 'Build the probe successfully before launch'}
  $probeBuildTime=(Get-Item -LiteralPath $ProbeDll).LastWriteTimeUtc
  if(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Where-Object {$_.LastWriteTimeUtc -gt $probeBuildTime}){throw 'Probe source is newer than its assembly; successful rebuild required before launch'}
}
$ProductDll=(Resolve-Path -LiteralPath $ProductDll).Path
$ProbeDll=(Resolve-Path -LiteralPath $ProbeDll).Path
if(-not(Test-Path -LiteralPath (Join-Path $runtimeRoot 'MAPGENAI_HEADLESS_OWNED'))){throw 'Owned runtime marker required'}
foreach($directory in @($runtimeRoot,(Join-Path $runtimeRoot 'Mods'))){
  if((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint){throw ('Owned output parent cannot be a link: '+$directory)}
}
if(-not(Test-Path -LiteralPath (Join-Path $runtimeRoot 'RimWorldWin64.exe'))){throw 'Owned executable missing'}
if(Get-CimInstance Win32_Process -Filter "name = 'RimWorldWin64.exe'" | Where-Object {$_.ExecutablePath -eq (Join-Path $runtimeRoot 'RimWorldWin64.exe')}){throw 'Another owned runtime process is active'}
$profile=Join-Path $runtimeRoot ('native-visual-profile-'+$Run)
$analysisRoot=Join-Path $PSScriptRoot '../runs'
if((Test-Path -LiteralPath $analysisRoot) -and ((Get-Item -LiteralPath $analysisRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Analysis output parent cannot be a link'}
$output=Join-Path $analysisRoot $Run
$mod=Join-Path $runtimeRoot ('Mods/NativeVisualProbe-'+$Run)
foreach($path in @($profile,$output,$mod)){if(Test-Path -LiteralPath $path){throw ('Fresh path required: '+$path)}}
# Additions are inside the marked private runtime only. No installed game Mods or user configs.
New-Item -ItemType Directory -Path (Join-Path $profile 'Config'),$output,(Join-Path $mod 'About'),(Join-Path $mod 'Assemblies') -Force | Out-Null
[xml]$about=Get-Content -LiteralPath (Join-Path $repoRoot 'dev/About/About.xml') -Raw
$packageId='choco.mapgenai.nativevisualprobe.'+$Run.Replace('-','')
$about.ModMetaData.packageId=$packageId
$about.ModMetaData.name='MapGenAI disposable shoreline audit'
$about.Save((Join-Path $mod 'About/About.xml'))
Copy-Item -LiteralPath $ProductDll -Destination (Join-Path $mod 'Assemblies/MapGenAI.dll')
Copy-Item -LiteralPath $ProbeDll -Destination (Join-Path $mod 'Assemblies/MapGenAI.NativeVisualProbe.dll')
Copy-Item -LiteralPath (Join-Path $repoRoot 'dev/Languages') -Destination $mod -Recurse
$active=@('brrainz.harmony','ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey','m00nl1ght.mappreview',$packageId)
$version=(Get-Content -LiteralPath (Join-Path $runtimeRoot 'Version.txt') -Raw).Trim()
$config='<ModsConfigData><version>'+$version+'</version><activeMods>'+(($active|ForEach-Object {'<li>'+$_+'</li>'}) -join '')+'</activeMods></ModsConfigData>'
[IO.File]::WriteAllText((Join-Path $profile 'Config/ModsConfig.xml'),$config)
[IO.File]::WriteAllText((Join-Path $profile 'Config/Prefs.xml'),'<Prefs><langFolderName>Korean (한국어)</langFolderName><runInBackground>true</runInBackground></Prefs>')
[IO.File]::WriteAllText((Join-Path $profile 'MAPGENAI_DISPOSABLE'),'Owned shoreline audit. No user saves or provider settings.')
$arguments=@(('-mapgenAINativeCase='+$Case),('-mapgenAINativeBiome='+$Biome),('-mapgenAINativeDetails='+$Details),('-mapgenAINativeLayout='+$InteractionLayout),'-batchmode','-nographics',('-savedatafolder="'+$profile+'"'),('-mapgenAINativeProbe="'+$output+'"'),'-logFile',('"'+(Join-Path $output 'Player.log')+'"'))
if($StateFile){$StateFile=(Resolve-Path -LiteralPath $StateFile).Path;$arguments+=('-mapgenAINativeState="'+$StateFile+'"')}
if($WaterProfile){$arguments+=('-mapgenAINativeWaterProfile='+$WaterProfile)}
$arguments+=('-mapgenAINativeWaterFill='+$WaterFill)
$arguments+=('-mapgenAINativeTileContext='+$TileContext)
if($FieldChecks){$arguments+='-mapgenAINativeFieldChecks=true'}
if($IntegrationChecks){$arguments+='-mapgenAINativeIntegrationChecks=true'}
if($BypassBlend){$arguments+='-mapgenAINativeBypass=true'}
if($Graphics){$arguments=@($arguments|Where-Object {$_ -ne '-nographics'})+@('-force-d3d11')}
function FileSha256([string]$Path){
  $algorithm=[Security.Cryptography.SHA256]::Create()
  try{return [BitConverter]::ToString($algorithm.ComputeHash([IO.File]::ReadAllBytes($Path))).Replace('-','')}
  finally{$algorithm.Dispose()}
}
# Resolve evidence before starting a game; a missing optional PowerShell module must not
# leave an unrecorded process running when the runner is called from another runtime.
$productHash=FileSha256 $ProductDll
$probeHash=FileSha256 $ProbeDll
$process=Start-Process -FilePath (Join-Path $runtimeRoot 'RimWorldWin64.exe') -WorkingDirectory $runtimeRoot -ArgumentList $arguments -WindowStyle Hidden -PassThru
@{pid=$process.Id;root=$runtimeRoot;repo=$repoRoot;profile=$profile;output=$output;case=$Case;biome=$Biome;details=$Details;interactionLayout=$InteractionLayout;bypassBlend=[bool]$BypassBlend;stateFile=$StateFile;waterProfile=$WaterProfile;waterFill=$WaterFill;tileContext=$TileContext;fieldChecks=[bool]$FieldChecks;integrationChecks=[bool]$IntegrationChecks;graphicsEnabled=[bool]$Graphics;arguments=$arguments;sourceDll=$ProductDll;sourceDllSha256=$productHash;probeDllSha256=$probeHash;started=(Get-Date).ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'launch.json') -Encoding utf8
Write-Output ('Owned process '+$process.Id+'; result: '+$output)
