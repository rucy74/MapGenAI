"""Plot every saved response in the matched-legend benchmark; makes no API calls."""
from pathlib import Path
import json
import sys
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.patches import Patch
import numpy as np

root = Path(sys.argv[1])
colors = {'G': '#7daa50', 'M': '#50463c', 'W': '#1e5ad2', 'N': '#aaaeb4',
          'S': '#7bc4ea', 'R': '#4d622d', 'B': '#d1b772', 'H': '#86a895',
          'D': '#76563e', 'I': '#bbd6dd'}
plt.rcParams.update({'font.family': 'Malgun Gothic', 'font.size': 10, 'axes.titleweight': 'bold'})

def cells(path):
    grid = json.loads(path.read_text(encoding='utf-8-sig'))['state']['imageMap']
    rgb = [matplotlib.colors.to_rgb(colors[c]) for c in grid['cells']]
    return np.array(rgb).reshape(grid['height'], grid['width'], 3)

fig, axes = plt.subplots(2, 6, figsize=(16, 7.5))
fig.patch.set_facecolor('#f4f6fa')
metrics = {}
for model in ('25', '38'):
    for item in json.loads((root / f'provider-gemini{model}-legend/vision-results.json').read_text())['results']:
        if 'id' in item:
            metrics[(model, item['id'])] = item
for row, fixture in enumerate(('topdown', 'sketch')):
    source = root / 'provider-gemini38-legend'
    axes[row, 0].imshow(plt.imread(source / f'{fixture}.png'), interpolation='nearest')
    axes[row, 0].set_title('입력 지도' if row == 0 else '입력 그림')
    axes[row, 1].imshow(cells(source / f'{fixture}-truth.json'), origin='lower', interpolation='nearest')
    axes[row, 1].set_title('독립 정답 영역')
    for col, (model, run) in enumerate((('25', 1), ('25', 2), ('38', 1), ('38', 2)), 2):
        ax = axes[row, col]
        item = metrics[(model, f'{fixture}-{run}')]
        ax.set_title(f'Gemini {model[0]}.{model[1]} · {run}회')
        if 'error' in item:
            ax.set_facecolor('#fff0f0')
            ax.text(.5, .5, '응답 거부\n자기 교차 다각형', ha='center', va='center', transform=ax.transAxes, color='#a82435')
            ax.set_xlabel('유효 격자가 없어 IoU 제외', color='#a82435')
        else:
            ax.imshow(cells(root / f'provider-gemini{model}-legend/{fixture}-{run}-state.json'), origin='lower', interpolation='nearest')
            ax.set_xlabel(f"물 {item['waterIoU']:.1%}  ·  산 {item['mountainIoU']:.1%}")
    for ax in axes[row]:
        ax.set_xticks([]); ax.set_yticks([])
        for spine in ax.spines.values(): spine.set_color('#d5dbe5')
fig.suptitle('MapGenAI  |  같은 그림 · 같은 범례 · 실제 API 응답', x=.03, y=.98, ha='left', fontsize=20, fontweight='bold')
fig.text(.03, .915, '그림 2종 × 모델별 2회. 작은 표본의 개발 평가이며, 임의 사진 재현 성능으로 일반화할 수 없습니다.', fontsize=11, color='#48566a')
fig.legend(handles=[Patch(color=colors[c], label=name) for c, name in [('W','물'),('M','산'),('G','토양'),('N','기존 자연 지형 유지')]], loc='lower left', bbox_to_anchor=(.025,.058), ncol=4, frameon=False)
fig.text(.03, .035, 'IoU = 정답과 예측 영역의 교집합 ÷ 합집합. 2.5 이미지 thinkingBudget=0, 3.8 thinkingLevel=low, 두 모델 temperature=0.2.', fontsize=10, color='#48566a')
fig.subplots_adjust(left=.03,right=.99,top=.84,bottom=.16,hspace=.4,wspace=.18)
fig.savefig(root / 'model-comparison.png', dpi=180, facecolor=fig.get_facecolor())
fig.savefig(root / 'model-comparison.pdf', facecolor=fig.get_facecolor())
plt.close(fig)
print(root / 'model-comparison.png')
