"""Prepare fresh full-composition rock/ground detail experiments, no API calls."""
import argparse, json, pathlib
import numpy as np
from water_report import prepare
from build_catalog import build, write
from rock import rock_layer


def detail_prepare(folder):
    prepare(folder)
    build(folder, folder/'source-native-final', details=True)
    for name in ('a', 'b', 'c'):
        manifest=json.loads((folder/('transfer-'+name+'-manifest.json')).read_text(encoding='utf-8'))
        write(folder/('detail-'+name+'-manifest.json'), manifest)
    cells=np.full((100,100),'G')
    cells[20:60,20:60]='M';cells[5,5]='N'
    layer,_=rock_layer(cells,'TemperateForest')
    write(folder/'recipes/rock-guard.json', {'action':'generate','params':{},'rock_layer':layer})
    unknown,_=rock_layer(np.full((100,100),'N'),'TemperateForest')
    write(folder/'recipes/unknown-rock.json', {'action':'generate','params':{},'rock_layer':unknown})
    write(folder/'rock-controls-manifest.json', {'world_seed':'map-library-rock-controls-20261001','cases':[
        {'id':'rock-guard','biome':'TemperateForest','size':100,'command':'recipes/rock-guard.json'},
        {'id':'unknown-rock','biome':'TemperateForest','size':100,'command':'recipes/unknown-rock.json'}]})
    print(json.dumps({'detail_sources':6,'rock_controls':2,'target_manifests':3,'paid_api_calls':0}))


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--folder',type=pathlib.Path,required=True)
    detail_prepare(parser.parse_args().folder)
