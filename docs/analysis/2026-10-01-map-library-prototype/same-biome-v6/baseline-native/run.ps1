param(
  [Parameter(Mandatory=$true)][string]$Manifest,
  [Parameter(Mandatory=$true)][string]$Output,
  [switch]$WithGL,
  [string]$ProductDll='G:/SteamLibrary/steamapps/common/RimWorld/Mods/MapGenAI-Dev/Assemblies/MapGenAI.dll',
  [switch]$Archive
)
$ErrorActionPreference='Stop'
$runtimeRoot=[IO.Path]::GetFullPath('C:/Users/choco/Documents/Codex/2026-09-13/new-chat-2/work/mapgenai-headless-runtime')
$repoRoot=[IO.Path]::GetFullPath('F:/Projects/Rimworld/active/mapgen_ai')
$profileRoot=[IO.Path]::GetFullPath('F:/Projects/Rimworld/work/mapgenai-library-profiles')
if(-not(Test-Path -LiteralPath (Join-Path $runtimeRoot 'MAPGENAI_HEADLESS_OWNED'))){throw 'Owned runtime marker required'}
$outputRoot=[IO.Path]::GetFullPath((Join-Path $repoRoot 'docs/analysis/2026-10-01-map-library-prototype'))
$Output=[IO.Path]::GetFullPath($Output)
if(-not $Output.StartsWith($outputRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Output must be a child of the prototype analysis directory'}
if($Archive){
  $launch=Get-Content -LiteralPath (Join-Path $Output 'launch.json') -Raw | ConvertFrom-Json
  $target=[IO.Path]::GetFullPath($launch.mod)
  $profile=[IO.Path]::GetFullPath($launch.profile)
  if((Split-Path -Parent $target) -ne (Join-Path $runtimeRoot 'Mods') -or (Split-Path -Leaf $target) -notmatch '^MapGenAI_Probe_library_\d{8}-\d{6}-\d{3}$'){throw 'Invalid owned probe target'}
  if(-not $profile.StartsWith($profileRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid owned profile'}
  if(-not(Test-Path -LiteralPath (Join-Path $profile 'MAPGENAI_DISPOSABLE'))){throw 'Missing disposable marker'}
  if((Get-Content -LiteralPath (Join-Path $target 'MAPGENAI_PROBE_OWNED') -Raw).Trim() -ne $profile){throw 'Ownership marker mismatch'}
  if(Get-CimInstance Win32_Process -Filter "Name='RimWorldWin64.exe'" | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($profile)}){throw 'Owned game still running'}
  $archiveRoot=Join-Path $runtimeRoot 'ArchivedProbes'
  $destination=Join-Path $archiveRoot (Split-Path -Leaf $target)
  foreach($p in @($target,$archiveRoot)){if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked move path rejected'}}
  if((Split-Path -Parent ([IO.Path]::GetFullPath($destination))) -ne [IO.Path]::GetFullPath($archiveRoot)){throw 'Archive outside owned runtime'}
  if(Test-Path -LiteralPath $destination){throw 'Archive exists'}
  New-Item -ItemType Directory -Path $archiveRoot -Force | Out-Null
  Move-Item -LiteralPath $target -Destination $destination
  @{archived=$true;mod=$target;archive=$destination;profile=$profile;utc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'cleanup.json') -Encoding utf8
  return
}
if(Get-CimInstance Win32_Process -Filter "Name='RimWorldWin64.exe'" | Where-Object {$_.ExecutablePath -eq (Join-Path $runtimeRoot 'RimWorldWin64.exe')}){throw 'Another owned runtime is active'}
$Manifest=(Resolve-Path -LiteralPath $Manifest).Path
$ProductDll=(Resolve-Path -LiteralPath $ProductDll).Path
if((Get-FileHash -LiteralPath $ProductDll -Algorithm SHA256).Hash -ne '6343F3116063E4C6DEA2CDEFA16B9914ECF9BDE401B13012FDB90ECE04004EF6'){throw 'Unexpected DEV baseline DLL; review before running'}
$probeDll=[IO.Path]::GetFullPath('C:/Users/choco/Documents/Codex/2026-09-13/new-chat-2/work/mapgenai-headless-runtime/ArchivedProbes/MapGenAI_Probe_library_20261002-001534-680/Assemblies/MapGenAI.MapLibraryProbe.dll')
if(-not(Test-Path -LiteralPath $probeDll)){throw 'Verified archived baseline probe DLL missing'}
if((Get-FileHash -LiteralPath $probeDll -Algorithm SHA256).Hash -ne '55F196AACF7D73673F9B0570E8DB14A96EF6F6C825C9C233C15D950613E8AC32'){throw 'Archived baseline probe DLL SHA mismatch; rebuild is not the baseline'}
if((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Probe.cs') -Algorithm SHA256).Hash -ne 'A5C0F5D4BBDBCC3A5099A04535FBF6C88AA70FA120A15977C78C318365564CA7'){throw 'Frozen 67721ad source SHA mismatch'}
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$profile=Join-Path $profileRoot $stamp
$mod=Join-Path $runtimeRoot ('Mods/MapGenAI_Probe_library_'+$stamp)
foreach($p in @($runtimeRoot,(Join-Path $runtimeRoot 'Mods'),$outputRoot,$profileRoot)){if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw ('Linked parent rejected: '+$p)}}
foreach($p in @($Output,$profile,$mod)){if(Test-Path -LiteralPath $p){throw ('Fresh path required: '+$p)}}
New-Item -ItemType Directory -Path $Output,(Join-Path $profile 'Config'),(Join-Path $mod 'About'),(Join-Path $mod 'Assemblies') -Force | Out-Null
$package='choco.mapgenai.libraryprobe.'+$stamp.Replace('-','')
[xml]$about=Get-Content -LiteralPath (Join-Path $repoRoot 'dev/About/About.xml') -Raw
$about.ModMetaData.packageId=$package; $about.ModMetaData.name='MapGenAI disposable map-library prototype'
$about.Save((Join-Path $mod 'About/About.xml'))
Copy-Item -LiteralPath $ProductDll -Destination (Join-Path $mod 'Assemblies/MapGenAI.dll')
Copy-Item -LiteralPath $probeDll -Destination (Join-Path $mod 'Assemblies/MapGenAI.MapLibraryProbe.dll')
Copy-Item -LiteralPath (Join-Path $repoRoot 'dev/Languages') -Destination $mod -Recurse
$active=@('brrainz.harmony','ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey')
if($WithGL){
  # GL disables overlapping original graphs with Odyssey. The source-reference
  # profile uses GL alone for those features; transfer profiles retain Odyssey.
  $active=@($active | Where-Object {$_ -ne 'ludeon.rimworld.odyssey'})
  $active+='m00nl1ght.geologicallandforms'
}
$active+=@('m00nl1ght.mappreview',$package)
$known=@('ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey')
$config='<ModsConfigData><version>'+((Get-Content -LiteralPath (Join-Path $runtimeRoot 'Version.txt') -Raw).Trim())+'</version><activeMods>'+(($active|ForEach-Object {'<li>'+$_+'</li>'}) -join '')+'</activeMods><knownExpansions>'+(($known|ForEach-Object {'<li>'+$_+'</li>'}) -join '')+'</knownExpansions></ModsConfigData>'
[IO.File]::WriteAllText((Join-Path $profile 'Config/ModsConfig.xml'),$config,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $profile 'Config/Prefs.xml'),'<Prefs><langFolderName>English</langFolderName><runInBackground>true</runInBackground></Prefs>')
[IO.File]::WriteAllText((Join-Path $profile 'MAPGENAI_DISPOSABLE'),'Map-library experiment; no user saves, settings or API keys.')
[IO.File]::WriteAllText((Join-Path $mod 'MAPGENAI_PROBE_OWNED'),$profile)
$arguments=@('-batchmode','-force-d3d11','-screen-fullscreen','0',('-savedatafolder="'+$profile+'"'),('-mapgenAILibraryProbe="'+$Output+'"'),('-mapgenAILibraryManifest="'+$Manifest+'"'),'-logFile',('"'+(Join-Path $Output 'Player.log')+'"'))
$process=Start-Process -FilePath (Join-Path $runtimeRoot 'RimWorldWin64.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
$launch=@{pid=$process.Id;profile=$profile;mod=$mod;output=$Output;manifest=$Manifest;withGL=[bool]$WithGL;productDllSha256=(Get-FileHash -LiteralPath $ProductDll -Algorithm SHA256).Hash;probeDllSha256=(Get-FileHash -LiteralPath $probeDll -Algorithm SHA256).Hash;activeMods=$active;utc=[DateTime]::UtcNow.ToString('o')}
$launch | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'launch.json') -Encoding utf8
$launch | ConvertTo-Json
