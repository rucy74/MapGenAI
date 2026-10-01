import json, pathlib, tempfile, unittest
import numpy as np
from water import water_layer, observed_water, decode

def meta(name, water=False, supported=True, river=False):
    return {'def':name,'water':water,'supported':supported,'river':river,'temporary':False,'dangerous':False,'fertility':1 if name=='Soil' else 0}

class WaterTests(unittest.TestCase):
    def test_small_pool_and_depth_are_not_discarded_by_polygon_fragment_budget(self):
        names=np.full((20,20),'Soil',dtype=object);names[3,17]='WaterShallow';names[4,17]='WaterDeep'
        cells=np.full((20,20),'G');cells[3,17]='S';cells[4,17]='W'
        layer,receipt=water_layer(cells,names,{'terrains':[meta('Soil'),meta('WaterShallow',True),meta('WaterDeep',True)]},'TemperateForest')
        plane=decode(layer);self.assertEqual(plane[3,17],2);self.assertEqual(plane[4,17],3)
        self.assertEqual(receipt['source_water_cells'],2);self.assertEqual(receipt['water_fragment_omissions'],0)
        self.assertEqual(plane[17,3],1)
    def test_river_sea_and_unknown_do_not_clear_but_observed_dry_floor_proves_dry_space(self):
        names=np.array([['WaterMovingShallow','WaterOceanDeep','Missing','BuiltFloor','Soil','Granite_Rough','WaterShallow']],dtype=object)
        cells=np.array([list('SWGGGMS')]);native={'terrains':[meta('WaterMovingShallow',True,river=True),meta('WaterOceanDeep',True),
            meta('BuiltFloor',supported=False),meta('Soil'),meta('Granite_Rough'),meta('WaterShallow',True)]}
        layer,_=water_layer(cells,names,native,'TemperateForest')
        self.assertEqual(decode(layer).tolist(),[[0,0,0,1,1,1,2]])
    def test_image_unknown_is_preserved_and_confident_dry_does_not_fill_holes(self):
        layer,_=water_layer(np.array([list('GSWN')]),np.array([['Soil','','','']],dtype=object),{'terrains':[meta('Soil')]},'TemperateForest',image=True)
        self.assertEqual(decode(layer).tolist(),[[1,2,3,0]])
    def test_old_histogram_does_not_claim_water_coordinates(self):
        with tempfile.TemporaryDirectory() as root:
            path=pathlib.Path(root)/'terrain.json';path.write_text(json.dumps({'terrain_defs':{'WaterDeep':5}}))
            with self.assertRaises(ValueError):observed_water(path,np.array([list('W')]),{'terrains':[]})
    def test_invalid_mode_overlap_bounds_and_material_rejected(self):
        base={'schema_version':1,'mode':'source-composition','row_order':'south-first','width':2,'height':2,'runs':[]}
        for runs in ([{'start':0,'length':5,'kind':1}],[{'start':0,'length':1,'kind':4}],
                     [{'start':0,'length':2,'kind':1},{'start':1,'length':1,'kind':2}]):
            with self.assertRaises(ValueError):decode(dict(base,runs=runs))
        with self.assertRaises(ValueError):decode(dict(base,mode='clear-map'))
    def test_water_recipe_requires_actual_water_evidence_and_rejects_conflicts(self):
        from finalize_catalog import finalize
        with tempfile.TemporaryDirectory() as root:
            root=pathlib.Path(root);(root/'catalog.json').write_text(json.dumps({'entries':[{'id':'valley','requires_water_sidecar':True}]}))
            (root/'transfer-evaluation.json').write_text(json.dumps({'records':[{'run':'native','id':'valley','biome':'TemperateForest','size':250,'geometry_pass':True}]}))
            finalize(root);self.assertEqual(json.loads((root/'catalog.json').read_text())['entries'][0]['verified_profiles'],[])
            (root/'water-evaluation.json').write_text(json.dumps({'records':[{'run':'native','id':'valley','pass':False}]}))
            finalize(root);self.assertEqual(json.loads((root/'catalog.json').read_text())['entries'][0]['verified_profiles'],[])
            (root/'water-evaluation.json').write_text(json.dumps({'records':[{'run':'native','id':'valley','pass':True}]}))
            finalize(root);self.assertEqual(len(json.loads((root/'catalog.json').read_text())['entries'][0]['verified_profiles']),1)
    def test_actual_source_pond_position_mismatch_cannot_be_hidden_by_native_pond_union(self):
        from measure_transfer import compare
        source=np.zeros((20,20),dtype=bool);source[3:7,3:7]=True
        target=source.copy();target[13:17,13:17]=True
        self.assertEqual(compare(source,target)['iou'],.5)
        self.assertEqual(compare(source|target,target)['iou'],1)
    def test_source_layout_admission_does_not_require_intentionally_removed_native_ponds(self):
        from measure_transfer import measure
        with tempfile.TemporaryDirectory() as root:
            folder=pathlib.Path(root);source=folder/'source';run=folder/'native';source.mkdir();run.mkdir()
            original=np.full((20,20),'G');original[3:7,3:7]='S'
            native=np.full((20,20),'G');native[12:19,12:19]='S'
            entry={'id':'valley','requires_water_sidecar':True,'features':{'new_water':True,'new_mountains':False},
                   'source':{'reference_run':'source','reference_id':'valley'}}
            (folder/'catalog.json').write_text(json.dumps({'entries':[entry]}))
            for directory,suffix,cells in ((source,'',original),(run,'',original),(run,'-baseline',native)):
                (directory/('valley'+suffix+'-terrain.json')).write_text(json.dumps({'width':20,'height':20,'cells':''.join(cells.ravel())}))
            (run/'result.json').write_text(json.dumps({'ok':True,'results':[{'id':'valley','biome':'TemperateForest','size':20,'tile':0,'counts':{}}]}))
            (run/'valley-water-application.json').write_text(json.dumps({'protected_conflicts':0}))
            report=measure(folder,[run]);self.assertTrue(report['records'][0]['geometry_pass'])
            self.assertEqual(report['records'][0]['metrics']['wet_footprint']['iou'],1)

if __name__=='__main__':unittest.main(verbosity=2)
