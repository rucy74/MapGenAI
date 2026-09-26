from pathlib import Path
import copy, hashlib, json

root=Path(__file__).parent.parent
source=root/'runs/native-temperate-water-new04-graphics-01/state.json'
base=json.loads(source.read_text(encoding='utf-8-sig'))
out=root/'fixtures'
out.mkdir(exist_ok=True)
states={}
states['generic-native']=copy.deepcopy(base)
states['generic-native-off']=copy.deepcopy(base)
states['generic-native-off']['state']['elevationShapes'][0]['details']='none'
states['generic-legacy']=copy.deepcopy(base)
states['generic-legacy']['state']['elevationShapes'][0].pop('water_profile',None)
states['exact']=copy.deepcopy(states['generic-legacy'])
states['exact']['state']['elevationShapes'][0]['edge_roughness']=None
states['exact']['state']['elevationShapes'][0]['details']='none'
states['shallow-native']=copy.deepcopy(base)
states['shallow-native']['state']['elevationShapes'][0]['compositeOps'][0]['fill']='WaterShallow'
states['hotspring']=copy.deepcopy(states['generic-legacy'])
states['hotspring']['state']['elevationShapes'][0]['compositeOps'][0]['fill']='HotSpring'
states['ring-native']=copy.deepcopy(base)
ring=states['ring-native']['state']['elevationShapes'][0]
outer=copy.deepcopy(ring['compositeShapes'][0]); outer.update(id='outer',w=.50,h=.42)
inner=copy.deepcopy(outer); inner.update(id='inner',w=.24,h=.18)
ring['compositeShapes']=[outer,inner]
ring['compositeOps'][0].update(op='sub',s=None,a='inner',b=None,**{'from':'outer'})
manifest={'source':str(source),'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),
    'note':'Deterministic explicit fixtures derived from recorded full state. These are controlled inputs, not provider outputs or screenshots. Exact/legacy pairs use one file under both DLLs. Native vs legacy differs only by water_profile. Ring subtracts inner from outer; full map must retain enclosed dry island.'}
for name,state in states.items():
    p=out/(name+'.json')
    p.write_text(json.dumps(state,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    manifest[name]={'path':str(p),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()}
(out/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'fixtures':len(states),'directory':str(out)}))
