"""Audit MG28 real-game replay results and visible thumbnail captures; no provider calls."""
from pathlib import Path
import hashlib
import json
import re
from PIL import Image

repo = Path(__file__).resolve().parents[1]
root = repo / 'docs/analysis/2026-09-22-recommendation-controls'
read = lambda path: json.loads(path.read_text(encoding='utf-8-sig'))
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()

def colors(path):
    # Observed 1280x800 screenshots: sample inside each centered thumbnail,
    # excluding all text/buttons. A non-null CPU texture did not catch grey GPU output.
    with Image.open(path) as image:
        assert image.size == (1280, 800), path
        return [len(set(image.crop((x-60, 475, x+60, 595)).convert('RGB').getdata()))
                for x in (349, 640, 931)]

dll = sha(repo/'dev/Assemblies/MapGenAI.dll')
counts = []
screens = []
unchanged = []
for language in ('ko', 'en'):
    initial = root/f'native-{language}'
    final = root/f'native-final-{language}'
    result = read(final/'result.json')
    assert result['ok'] and result['error'] is None
    assert all(line.startswith('PASS ') for line in result['checks'])
    assert read(final/'launch.json')['sourceDllSha256'].lower() == dll
    counts.append(len(result['checks']))
    grey = colors(initial/'controls-expanded-ui.png')
    visible = colors(final/'controls-expanded-ui.png')
    assert grey == [1, 1, 1] and min(visible) > 5, (grey, visible)
    screens.append(dict(language=language, initialGreyColors=grey, finalThumbnailColors=visible))
    for scenario in ('recommend-plain', 'recommend-existing', 'native-features'):
        for number in (1, 2, 3):
            name = f'{scenario}-{number}.png'
            assert sha(initial/name) == sha(final/name), (language, name)
            unchanged.append(f'{language}/{name}')
    assert len([line for line in result['checks'] if '62500' in line]) == 10
    for name in ('controls-expanded-ui.png', 'controls-folded-ui.png', 'controls-small-620x520-ui.png'):
        assert (final/name).is_file()

offline = (root/'offline-tests.txt').read_text(encoding='utf-8-sig')
match = re.search(r'CoreRegressionTests: (\d+) PASS / 0 FAIL', offline)
assert match and int(match[1]) == 178
for launch in root.glob('*/launch.json'):
    assert read(launch.parent/'cleanup.json')['removed']
    assert not Path(read(launch)['mod']).exists()

summary = dict(ok=True, dllSha256=dll, offlineTests=178, nativeChecks=sum(counts),
               nativeChecksByLanguage=counts, newProviderCalls=0, fableCalls=0,
               thumbnailScreens=screens, unchangedOriginalImages=unchanged,
               selectedPreviewPixelMatches=20, screenshotsVisuallyReviewed=True,
               originalGreyRunsRetained=True, completeMapRuns=0,
               unresolvedPriorNativeCrashes=1)
(root/'evaluation.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print(json.dumps({k:v for k,v in summary.items() if k != 'unchangedOriginalImages'}, ensure_ascii=False))
