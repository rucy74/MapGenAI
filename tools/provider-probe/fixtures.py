"""Deterministic test drawings and independent ground-truth masks; no API credentials."""
from pathlib import Path
from PIL import Image, ImageDraw
import json, sys, random

out=Path(sys.argv[1]);out.mkdir(parents=True,exist_ok=True)
size=128
for name in ('topdown','sketch'):
    cells=['G']*(size*size)
    for z in range(size):
        for x in range(size):
            if name=='topdown':
                if 8<=x<40 and 78<=z<116: cells[z*size+x]='M'
                if 66<=x<116 and 12<=z<62: cells[z*size+x]='W'
                if 84<=x<98 and 30<=z<44: cells[z*size+x]='G'
            else:
                if ((x-29)/23)**2+((z-96)/21)**2<=1: cells[z*size+x]='M'
                if 67<=x<76 and 8<=z<120: cells[z*size+x]='W'
                if ((x-99)/21)**2+((z-30)/20)**2<=1: cells[z*size+x]='W'
                if 76<=x<83 and 26<=z<34: cells[z*size+x]='W'
    colors={'G':(125,170,80),'M':(80,70,60),'W':(30,90,210)}
    pic=Image.new('RGB',(size,size))
    pic.putdata([colors[cells[z*size+x]] for z in reversed(range(size)) for x in range(size)])
    pic=pic.resize((512,512),Image.Resampling.NEAREST)
    if name=='sketch':
        # Topographic hatching adds drawing texture without changing its annotated geometry.
        draw=ImageDraw.Draw(pic)
        for z in range(80,110,7):
            for x in range(18,40,7):
                if cells[z*size+x]=='M':draw.line((x*4,(127-z)*4,x*4+10,(127-z)*4-10),fill=(145,125,100),width=2)
    pic.save(out/(name+'.png'))
    (out/(name+'-truth.json')).write_text(json.dumps({'schema_version':2,'state':{'imageMap':{'width':size,'height':size,'cells':''.join(cells),'note':'Independent synthetic fixture ground truth'}}}),encoding='utf-8')
print('Created two annotated drawings with north-west mountains, south-east water, an island and a narrow channel.')
