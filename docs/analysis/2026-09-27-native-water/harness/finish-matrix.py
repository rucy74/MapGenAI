"""Finish two unrun fixtures after diagnosed native ThinIce overlay; preserve raw failures."""
from pathlib import Path
import ctypes, hashlib, json, subprocess, time
root=Path(__file__).parent.parent
read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
progress=read(root/'matrix-progress.json')
probe=root/'harness/bin/Debug/net472/MapGenAI.NativeVisualProbe.dll'
assert sha(probe)==progress['probeSha256']
new=root/'candidate-06.dll'
assert sha(new)==progress['newSha256']
kernel=ctypes.WinDLL('kernel32',use_last_error=True)
kernel.OpenProcess.argtypes=[ctypes.c_ulong,ctypes.c_int,ctypes.c_ulong];kernel.OpenProcess.restype=ctypes.c_void_p
kernel.WaitForSingleObject.argtypes=[ctypes.c_void_p,ctypes.c_ulong];kernel.WaitForSingleObject.restype=ctypes.c_ulong
kernel.CloseHandle.argtypes=[ctypes.c_void_p]
out={'initialMatrix':'matrix-progress.json','probeSha256':sha(probe),'jobs':[],
     'reason':'Old river failed raw all-layer equality only after native ThinIce seasonal surface/temp overlays; permanent top/under/foundation remain identical, and before/after authored blend all-layer values match. Raw failures stay intact.'}
def save(): (root/'matrix-completion.json').write_text(json.dumps(out,indent=2),encoding='utf-8')
for key,args in [('river-new',['-Case','pool','-WaterFill','water','-WaterProfile','native','-TileContext','river']),('ring-new',['-Case','pool','-StateFile',str(root/'fixtures/ring-native.json')])]:
    assert sha(probe)==progress['probeSha256']
    run='native-final-'+key+'-01';folder=root/'runs'/run
    if folder.exists():raise RuntimeError('Fresh run required: '+str(folder))
    cmd=['powershell','-NoProfile','-ExecutionPolicy','Bypass','-File',str(root/'harness/run.ps1'),'-Run',run,'-ProductDll',str(new),'-Graphics']+args
    row={'id':key,'run':run,'command':cmd,'startedEpoch':time.time()};out['jobs'].append(row);save()
    launched=subprocess.run(cmd,capture_output=True,text=True,encoding='utf-8',errors='replace');row['launcherExit']=launched.returncode;row['launcherOutput']=launched.stdout+launched.stderr;save()
    print(run,'launch',launched.returncode,flush=True)
    if launched.returncode:raise RuntimeError(row['launcherOutput'])
    receipt=read(folder/'launch.json');row['pid']=receipt['pid'];save()
    handle=kernel.OpenProcess(0x100000,False,receipt['pid']);deadline=time.monotonic()+480
    try:
        while not (folder/'result.json').exists() or handle and kernel.WaitForSingleObject(handle,0)==258:
            if time.monotonic()>deadline:raise RuntimeError('Timeout; process left untouched')
            time.sleep(1)
    finally:
        if handle:kernel.CloseHandle(handle)
    result=read(folder/'result.json');row.update(ok=result['ok'],checks=len(result['checks']),error=result['error'],endedEpoch=time.time());save()
    if key=='river-new':
        final=[v for count,v in read(folder/'full-complete-terrain.json')['layers'] for _ in range(count)]
        native=read(folder/'full-native-river.json')['cells']
        row['permanentRiverLayersUnchanged']=len(native)>0 and all(c['layers'].split('|')[1:4]==final[c['index']].split('|')[1:4] for c in native)
        row['onlyExpectedRawIceFailures']=all(c['ok'] or c['name'] in ['full-phase-end: native river terrain layers preserved','full-complete: native river terrain layers preserved'] for c in result['checks'])
        save()
        if not row['permanentRiverLayersUnchanged'] or not row['onlyExpectedRawIceFailures'] or result['error']:raise RuntimeError('Unexpected river result; preserved')
    elif not result['ok']:raise RuntimeError('Ring failure; preserved')
    print(run,'raw PASS' if row['ok'] else 'raw FAIL, diagnosed native ice',flush=True)
out['complete']=True;save()
