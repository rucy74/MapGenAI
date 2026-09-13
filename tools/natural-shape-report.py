"""Report native generated terrain masks; never substitutes a drawn/AI image for game output."""
from pathlib import Path
import json, math
from PIL import Image, ImageDraw, ImageFont

root=Path(__file__).resolve().parents[1]/'docs/analysis/2026-09-13-natural-shapes'
native=root/'native-r2'
source=Image.open(native/'natural-shapes-generated-preview.png').convert('RGB')
terrain=json.loads((native/'natural-actual-terrain.json').read_text(encoding='utf-8'))
before=json.loads((root/'native/natural-shapes-generated-state.json').read_text(encoding='utf-8'))['state']['imageMap']['cells']
cells=terrain['cells'];n=512
rows=[]
for row,kind in enumerate(['circle','star','heart','donut']):
    low=(3-row)*128; high=low+128
    a=[cells[y*n+x]=='W' for y in range(low,high) for x in range(256)]
    b=[cells[y*n+x+256]=='W' for y in range(low,high) for x in range(256)]
    union=sum(x or y for x,y in zip(a,b));overlap=sum(x and y for x,y in zip(a,b))
    old=[before[y*n+x]=='W' for y in range(low,high) for x in range(256)]
    cross_run_changed=sum(x!=y for x,y in zip(a,old))
    changed=sum(x!=y for x,y in zip(a,b));assert changed>30
    rows.append(dict(shape=kind,preciseArea=sum(a),naturalArea=sum(b),changedCells=changed,intersectionOverUnion=overlap/union,preciseCrossRunChangedCells=cross_run_changed))
receipt={'nativeChecks':len(json.loads((native/'result.json').read_text())['checks']),'rows':rows,'scope':'Same native map, paired column footprints aligned by 256 cells; exact half also compared to previous native run. Visual judgment required for naturalness.'}
(root/'native-comparison.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')

fontpath='C:/Windows/Fonts/malgun.ttf'
title=ImageFont.truetype(fontpath,30);body=ImageFont.truetype(fontpath,21);small=ImageFont.truetype(fontpath,17)
canvas=Image.new('RGB',(900,940),'#17202a');draw=ImageDraw.Draw(canvas)
draw.text((40,24),'실제 RimWorld 생성 비교',font=title,fill='white')
draw.text((245,82),'기본 도형',font=body,fill='#d5dbe3')
draw.text((580,82),'“자연스러운” 도형',font=body,fill='#87dbb5')
for row,label in enumerate(['원형','별','하트','도넛']):
    y=137+row*181;draw.text((34,y+59),label,font=body,fill='white')
    for col in range(2):
        # Crops remain native pixels; nearest-neighbor magnification only.
        crop=source.crop((col*256+51,row*128+4,col*256+205,row*128+124)).resize((231,180),Image.Resampling.NEAREST)
        canvas.paste(crop,(200+col*335,y))
draw.text((40,886),'왼쪽·오른쪽 모두 게임이 생성한 물 지형입니다. 오른쪽은 기본 자연스러움 강도.',font=small,fill='#bbc6d4')
draw.text((40,914),'비교를 위해 토양 바탕을 고정했습니다. 실제 맵의 다른 생성 요소는 영향을 줄 수 있습니다.',font=small,fill='#bbc6d4')
canvas.save(root/'comparison.png')
print(json.dumps(receipt,indent=2))
