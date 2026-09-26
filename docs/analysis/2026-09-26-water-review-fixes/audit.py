"""Re-audit the final controlled runs with the currently built DEV DLL."""
from pathlib import Path
import subprocess,sys
root=Path(__file__).resolve().parent
repo=root.parents[2]
runs=['shore-temperate-01','shore-desert-01','shore-cold-01','shore-boreal-01',
      'shore-protected-01','shore-none-01','shore-bypass-01','shore-hotspring-01',
      'shore-cardinal-reference-01','shore-cardinal-water-01','shore-cardinal-connected-01',
      'shore-cardinal-explicit-01','shore-cardinal-special-01']
subprocess.run([sys.executable,str(repo/'tools/shoreline-probe/evaluate.py'),'--root',str(root),
 '--runs',*runs,'--natural','shore-temperate-01','--bypass','shore-bypass-01','--off','shore-none-01',
 '--interactions'],check=True)
subprocess.run([sys.executable,str(root/'compare-baseline.py')],check=True)
