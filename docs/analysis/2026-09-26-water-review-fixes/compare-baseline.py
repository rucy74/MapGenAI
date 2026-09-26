from pathlib import Path
import json
root=Path(__file__).resolve().parent
def read(run,name):return json.loads((root/('native-'+run)/name).read_text(encoding='utf-8-sig'))
def expanded(data):return [v for n,v in data['layers'] for _ in range(n)]
rows=[]
for biome in ('temperate','arid'):
    for version in ('old','new'):
        if biome=='temperate':
            reference='shore-old-reference-01' if version=='old' else 'shore-near-reference-01'
            water='shore-old-water-01' if version=='old' else 'shore-near-water-01'
        else:
            prefix='shore-arid-old-' if version=='old' else 'shore-arid-'
            reference,water=prefix+'reference-01',prefix+'water-01'
        if not (root/('native-'+water)/'result.json').exists():continue
        for phase in ('preview','full'):
            a=read(reference,phase+'-before-blend.json');b=read(water,phase+'-before-blend.json')
            av,bv=expanded(a),expanded(b)
            aa=expanded(read(reference,phase+'-after-blend.json'));bb=expanded(read(water,phase+'-after-blend.json'))
            inputs=[i for i in range(62500) if av[i]!=bv[i]]
            common=[i for i in range(62500) if av[i]==bv[i] and av[i].split('|')[0] in ('Soil','Sand')]
            changes=[i for i in common if aa[i]!=bb[i]]
            control=read(water,phase+'-injected-interaction.json')
            assert set(inputs)==set(control['patchCells']) and len(inputs)==9
            assert a['elevationHash']==b['elevationHash'] and a['cavesHash']==b['cavesHash']
            rows.append(dict(biome=biome,version=version,phase=phase,inputDifferenceCells=len(inputs),commonDryCells=len(common),influencedCells=changes))
(root/'baseline-comparison.json').write_text(json.dumps(rows,indent=2)+'\n',encoding='utf-8')
assert len(rows)==8
assert all((len(r['influencedCells'])>0 if r['version']=='old' else not r['influencedCells']) for r in rows)
print(json.dumps(rows))
