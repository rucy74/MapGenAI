$ErrorActionPreference='Stop'
$taskOutput=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ownedRuntime=[IO.Path]::GetFullPath('C:/Users/choco/Documents/Codex/2026-09-13/new-chat-2/work/mapgenai-headless-runtime')
$modRoot=[IO.Path]::GetFullPath((Join-Path $ownedRuntime 'Mods'))
$archiveRoot=[IO.Path]::GetFullPath((Join-Path $ownedRuntime 'ArchivedProbes/native-generation-2026-09-27'))
if(!(Test-Path -LiteralPath (Join-Path $ownedRuntime 'MAPGENAI_HEADLESS_OWNED'))){throw 'Owned runtime marker missing'}
foreach($directory in @($ownedRuntime,$modRoot)){
 if((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Refusing reparse point'}
}
$runtimeExe=[IO.Path]::GetFullPath((Join-Path $ownedRuntime 'RimWorldWin64.exe'))
$processes=@(Get-CimInstance Win32_Process)
if(@($processes|Where-Object {$_.ExecutablePath -and [IO.Path]::GetFullPath($_.ExecutablePath) -eq $runtimeExe}).Count){throw 'Owned runtime process still active; no move performed'}
$plans=@()
foreach($receiptFile in Get-ChildItem -LiteralPath (Join-Path $taskOutput 'runs') -Filter launch.json -File -Recurse){
 $receipt=Get-Content -LiteralPath $receiptFile.FullName -Raw|ConvertFrom-Json
 if([IO.Path]::GetFullPath($receipt.root) -ne $ownedRuntime){throw 'Receipt points outside owned runtime'}
 $name='NativeVisualProbe-'+$receiptFile.Directory.Name
 if($name -notmatch '^NativeVisualProbe-native-[a-zA-Z0-9-]+$'){throw 'Unexpected probe folder name'}
 $source=[IO.Path]::GetFullPath((Join-Path $modRoot $name))
 $destination=[IO.Path]::GetFullPath((Join-Path $archiveRoot $name))
 if(!($source.StartsWith($modRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) -or !($destination.StartsWith($archiveRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase))){throw 'Resolved move outside approved roots'}
 $matching=@($processes|Where-Object {$_.ProcessId -eq $receipt.pid})
 if($matching.Count -and (!$matching[0].ExecutablePath -or [IO.Path]::GetFullPath($matching[0].ExecutablePath) -eq $runtimeExe)){throw 'Launch PID still active or ambiguous'}
 if(!(Test-Path -LiteralPath $source)){throw 'Expected receipt source missing'}
 if(Test-Path -LiteralPath $destination){throw 'Archive destination already exists'}
 if((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Probe source reparse point'}
 $descendants=@(Get-ChildItem -LiteralPath $source -Recurse -Force)
 if(@($descendants|Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){throw 'Probe descendant reparse point'}
 $hashes=@($descendants|Where-Object {!$_.PSIsContainer}|ForEach-Object {[ordered]@{relative=$_.FullName.Substring($source.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
 $plans+=,[ordered]@{source=$source;destination=$destination;receipt=$receiptFile.FullName;launchPid=$receipt.pid;launchPidGoneOrReusedByOtherExecutable=$true;files=$hashes}
}
# All absolute targets, ownership markers, PIDs and symlinks were verified before any move.
New-Item -ItemType Directory -Path $archiveRoot -Force|Out-Null
if((Get-Item -LiteralPath $archiveRoot).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Archive reparse point'}
foreach($plan in $plans){
 Move-Item -LiteralPath $plan.source -Destination $plan.destination
 foreach($file in $plan.files){if((Get-FileHash -LiteralPath (Join-Path $plan.destination $file.relative) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256){throw 'Archived file hash mismatch'}}
 $plan['moved']=$true
}
$report=[ordered]@{created=(Get-Date).ToString('o');ownedRuntime=$ownedRuntime;runtimeExecutable=$runtimeExe;runtimeProcessAbsent=$true;archiveRoot=$archiveRoot;count=$plans.Count;plans=$plans;scope='Only NativeVisualProbe folders named by this task launch receipts. No user processes, user config or actual installed game Mods touched. All outputs retained.'}
$report|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $taskOutput 'probe-cleanup.json') -Encoding UTF8
Write-Output ('Archived '+$plans.Count+' owned probe directories; all file hashes match.')
