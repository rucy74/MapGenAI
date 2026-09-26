param([Parameter(Mandatory=$true)][string]$ProductDll,[Parameter(Mandatory=$true)][string]$BaselineDll,[ValidatePattern('^[a-z0-9-]+$')][string]$Suffix='s4-r5')
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$evidence=Join-Path $repo 'docs/analysis/2026-09-23-landscape-blending'
$jobs=@(@('A','null-old'),@('A','null-new'),@('A','natural'),@('B','natural'),@('C','natural'),@('D','natural'),@('protected','natural'),@('road','natural'))
foreach($job in $jobs){
  $case=$job[0];$mode=$job[1];$run='blend-'+$case.ToLowerInvariant()+'-'+$mode+'-'+$Suffix
  $dll=$ProductDll;$details=$mode
  if($mode -eq 'null-old'){$dll=$BaselineDll;$details='null'}
  if($mode -eq 'null-new'){$details='null'}
  & (Join-Path $PSScriptRoot 'run.ps1') -Run $run -ProductDll $dll -Case $case -Details $details -Graphics
  $folder=Join-Path $evidence ('native-'+$run)
  $launch=Get-Content -LiteralPath (Join-Path $folder 'launch.json') -Raw|ConvertFrom-Json
  $process=Get-Process -Id $launch.pid -ErrorAction SilentlyContinue
  if($process){
    if($process.Path -ne (Join-Path $launch.root 'RimWorldWin64.exe')){throw 'Owned process identity mismatch'}
    $deadline=(Get-Date).AddMinutes(6)
    while(-not $process.HasExited){
      if((Get-Date) -gt $deadline){throw ('Owned run exceeded six minutes: '+$run+' PID '+$launch.pid)}
      Start-Sleep -Seconds 2
    }
  }
  $resultFile=Join-Path $folder 'result.json'
  if(-not(Test-Path -LiteralPath $resultFile)){throw ('No result for '+$run+'; inspect retained Player.log')}
  $result=Get-Content -LiteralPath $resultFile -Raw|ConvertFrom-Json
  Write-Output ($run+': ok='+$result.ok+' checks='+$result.checks.Count+' error='+$result.error)
}
