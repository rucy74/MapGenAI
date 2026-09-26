"""Finite serial actual-preview/full-map regression run. Does not stop any process."""
from pathlib import Path
import ctypes, hashlib, json, subprocess, time

root=Path(__file__).parent.parent
harness=root/'harness'
old=root/'baseline-21e5614.dll'
new=root/'candidate-06.dll'
probe=harness/'bin/Debug/net472/MapGenAI.NativeVisualProbe.dll'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
expected_new='4908863336befac9c141ba35cb15bbfb47617ed87a9236cd21f7b3449abde019'
if sha(new)!=expected_new:raise RuntimeError('Unexpected frozen new product hash')
if sha(old)!='b75da4efe80ce1333b75538a669ea0811dc4c81b98976bb94a15beba1cb5e084':raise RuntimeError('Unexpected frozen old product hash')
probe_hash=sha(probe)
gate=root/'runs/native-temperate-water-new06-graphics-01'
gate_result=read(gate/'result.json')
field=read(gate/'native-water-checks.json')
if not gate_result['ok'] or field['passed']!=706 or field['failed']!=0:raise RuntimeError('Candidate 06 property/integration gate has not passed')

jobs=[
 ('temp-old',old,['-Case','pool','-WaterFill','water']),
 ('desert-old',old,['-Case','pool','-Biome','desert','-WaterFill','water']),
 ('desert-new',new,['-Case','pool','-Biome','desert','-WaterFill','water','-WaterProfile','native']),
 ('exact-old',old,['-Case','exact','-StateFile',str(root/'fixtures/exact.json')]),
 ('exact-new',new,['-Case','exact','-StateFile',str(root/'fixtures/exact.json')]),
 ('legacy-new',new,['-Case','pool','-StateFile',str(root/'fixtures/generic-legacy.json')]),
 ('off-new',new,['-Case','pool','-StateFile',str(root/'fixtures/generic-native-off.json')]),
 ('shallow-new',new,['-Case','pool','-StateFile',str(root/'fixtures/shallow-native.json')]),
 ('protected-old',old,['-Case','protected','-WaterFill','water']),
 ('protected-new',new,['-Case','protected','-WaterFill','water','-WaterProfile','native']),
 ('hotspring-old',old,['-Case','hotspring']),
 ('hotspring-new',new,['-Case','hotspring']),
 ('river-old',old,['-Case','pool','-WaterFill','water','-TileContext','river']),
 ('river-new',new,['-Case','pool','-WaterFill','water','-WaterProfile','native','-TileContext','river']),
 ('ring-new',new,['-Case','pool','-StateFile',str(root/'fixtures/ring-native.json')]),
]
progress={'newDll':str(new),'newSha256':expected_new,'oldDll':str(old),'oldSha256':sha(old),'probeSha256':probe_hash,
          'reusedCurrentTemperateRun':str(gate),'jobs':[],'notes':'Actual native maps; graphics enabled. No aesthetic pass inferred from tests. Results never overwritten; no automatic kill or retry.'}
progress_path=root/'matrix-progress.json'
def save():progress_path.write_text(json.dumps(progress,ensure_ascii=False,indent=2),encoding='utf-8')
kernel=ctypes.WinDLL('kernel32',use_last_error=True)
kernel.OpenProcess.argtypes=[ctypes.c_ulong,ctypes.c_int,ctypes.c_ulong];kernel.OpenProcess.restype=ctypes.c_void_p
kernel.WaitForSingleObject.argtypes=[ctypes.c_void_p,ctypes.c_ulong];kernel.WaitForSingleObject.restype=ctypes.c_ulong
kernel.CloseHandle.argtypes=[ctypes.c_void_p]
for name,dll,args in jobs:
    if sha(probe)!=probe_hash:raise RuntimeError('Probe changed during frozen matrix')
    run='native-final-'+name+'-01'
    folder=root/'runs'/run
    if folder.exists():raise RuntimeError('Fresh run required: '+str(folder))
    command=['powershell','-NoProfile','-ExecutionPolicy','Bypass','-File',str(harness/'run.ps1'),'-Run',run,'-ProductDll',str(dll),'-Graphics']+args
    row={'id':name,'run':run,'command':command,'startedEpoch':time.time()};progress['jobs'].append(row);save()
    launched=subprocess.run(command,capture_output=True,text=True,encoding='utf-8',errors='replace')
    row['launcherExit']=launched.returncode;row['launcherOutput']=launched.stdout+launched.stderr;save()
    print(run,'launched',launched.returncode,flush=True)
    if launched.returncode:raise RuntimeError(row['launcherOutput'])
    receipt=read(folder/'launch.json');row['pid']=receipt['pid'];save()
    handle=kernel.OpenProcess(0x100000,False,receipt['pid'])
    deadline=time.monotonic()+480
    try:
        while not (folder/'result.json').exists() or handle and kernel.WaitForSingleObject(handle,0)==258:
            if time.monotonic()>deadline:raise RuntimeError('Timed out; owned process left untouched: '+str(receipt['pid']))
            time.sleep(1)
    finally:
        if handle:kernel.CloseHandle(handle)
    result=read(folder/'result.json');row['ok']=result['ok'];row['checks']=len(result['checks']);row['error']=result['error'];row['endedEpoch']=time.time();save()
    print(run,'PASS' if row['ok'] else 'FAIL',row['error'],flush=True)
    if not row['ok']:raise RuntimeError('Preserved failed run: '+run)
progress['complete']=True;save()
print('Matrix completed:',len(jobs),'new runs + 1 reused candidate06 run',flush=True)
