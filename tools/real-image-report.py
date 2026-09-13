"""Compare saved real-input model states with references and actual native previews.

No model calls. Landmark annotations are manual/non-blind and not full segmentation truth.
Plan-to-generation measurements do not measure original-image interpretation accuracy.
"""
from pathlib import Path
import json
import sys
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from PIL import Image

root=Path(sys.argv[1]) if len(sys.argv)>1 else Path(__file__).resolve().parents[1]/'docs/analysis/2026-09-13-real-inputs'
plt.rcParams['font.family']='Malgun Gothic'
inputs=json.loads((root/'inputs.json').read_text('utf-8'))['inputs']
colors={'N':'#969696','M':'#514437','W':'#2787b6','S':'#73b9ca','G':'#a4bc76','R':'#649358','B':'#d0bf88','H':'#75a592','D':'#896c51','I':'#d3e9e5'}
def state(path):return json.loads(path.read_text('utf-8'))['state']['imageMap']
def rgb(s):return np.array([matplotlib.colors.to_rgb(colors[c]) for c in s['cells']]).reshape(s['height'],s['width'],3)
fig,axes=plt.subplots(3,4,figsize=(14,10))
generation=[]
runs={'preview_river':'generated-preview_river','gorge':'generated-gorge','ai_map':'generated-valley-fixed'}
for row,e in enumerate(inputs):
 name=e['id'];axes[row,0].imshow(Image.open(root/e['path']));axes[row,0].set_title('원본 · '+name)
 old=root/'baseline-38'/(name+'-state.json')
 if old.exists():axes[row,1].imshow(rgb(state(old)),origin='lower')
 else:axes[row,1].text(.5,.5,'다각형 좌표 오류\n적용 거부',ha='center',va='center',color='crimson')
 axes[row,1].set_title('기존 해석 · Gemini 3.8')
 planned=state(root/'final-38'/(name+'-state.json'))
 axes[row,2].imshow(rgb(planned),origin='lower');axes[row,2].set_title('새 분류도 · Gemini 3.8')
 run=root/runs[name]
 axes[row,3].imshow(Image.open(run/(name+'-generated-preview.png')),interpolation='nearest');axes[row,3].set_title('실제 생성 · native Map Preview')
 generated=state(run/(name+'-generated-state.json'))
 want=np.array(list(planned['cells'])).reshape(planned['height'],planned['width'])
 want=want[(np.arange(generated['height'])*planned['height']//generated['height'])[:,None],(np.arange(generated['width'])*planned['width']//generated['width'])[None,:]]
 actual=np.array(list(generated['cells'])).reshape(generated['height'],generated['width'])
 valid=want!='N';item={'id':name,'run':runs[name],'scope':'Plan to actual generation, not original-image fidelity','comparedCells':int(valid.sum())}
 for label in ['M','W']:
  target=np.isin(want,['W','S']) if label=='W' else want=='M';seen=actual==label
  inter=int((target&seen&valid).sum());union=int(((target|seen)&valid).sum())
  item[label]={'intersection':inter,'union':union,'iou':inter/union if union else 1,'missing':int((target&~seen&valid).sum()),'extra':int((~target&seen&valid).sum())}
 generation.append(item)
 for ax in axes[row]:ax.set_xticks([]);ax.set_yticks([])
fig.suptitle('읽을 수 있는 맵 참고 이미지 · 원본 해석과 실제 생성은 별도 평가',fontsize=15)
fig.tight_layout();fig.savefig(root/'real-input-comparison.png',dpi=155);plt.close(fig)
(root/'generation-comparison.json').write_text(json.dumps(generation,ensure_ascii=False,indent=2),'utf-8')
for item in generation:print(item)
