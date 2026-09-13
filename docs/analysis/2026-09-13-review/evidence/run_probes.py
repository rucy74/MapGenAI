from pathlib import Path
import subprocess, json, datetime
root=Path(__file__).parent
project=root/'probes/ParserProbe.csproj'
build=subprocess.run(['dotnet','build',str(project),'--nologo'],capture_output=True,text=True,encoding='utf-8')
(root/'build-parser-probe.log').write_text(build.stdout+build.stderr,encoding='utf-8')
if build.returncode: raise SystemExit(build.stdout+build.stderr)
dll=root/'probes/bin/Debug/net10.0/ParserProbe.dll'
normal=subprocess.run(['dotnet',str(dll)],capture_output=True,text=True,encoding='utf-8',timeout=10)
try:
 hang=subprocess.run(['dotnet',str(dll),'hang'],capture_output=True,text=True,encoding='utf-8',timeout=2)
 hang_result={'timed_out':False,'exit_code':hang.returncode,'stdout':hang.stdout}
except subprocess.TimeoutExpired:
 hang_result={'timed_out':True,'timeout_seconds':2,'process_killed':True}
result={'timestamp':datetime.datetime.now().astimezone().isoformat(),
 'scope':'Synthetic adversarial inputs against unchanged v1.6 SimpleJson.cs; not live traffic frequency or in-game validation.',
 'build_exit':build.returncode,'normal_exit':normal.returncode,'normal_stdout':normal.stdout,'malformed_array':hang_result}
(root/'parser-probes.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(result,ensure_ascii=False,indent=2))
