import json, pathlib, tempfile, unittest
import numpy as np
from PIL import Image
from ground import ground_layer, observed_ground, image_palette, read_image_materials

def terrain(name,rgb=(90,70,50),supported=True,water=False,temporary=False,color=True):
    return {'def':name,'rgb':list(rgb) if color else None,'supported':supported,'water':water,
            'temporary':temporary,'has_preview_color':color,'fertility':0}

class GroundTests(unittest.TestCase):
    def image(self,colors,pixels):
        with tempfile.TemporaryDirectory() as root:
            root=pathlib.Path(root);Image.fromarray(np.asarray(pixels,dtype=np.uint8)).save(root/'map.png')
            (root/'palette.json').write_text(json.dumps({'colors':colors,'max_distance':2,'min_margin':1}))
            return read_image_materials(root/'map.png',root/'palette.json')
    def test_same_rgb_different_ground_is_unknown_not_nearest_guess(self):
        colors=[{'label':'G','terrain':name,'rgb':[90,70,50]} for name in ('Soil','Gravel')]
        cells,names,receipt=self.image(colors,[[[90,70,50]]])
        self.assertEqual(cells[0,0],'N');self.assertEqual(names[0,0],'');self.assertEqual(receipt['ambiguous_pixels'],1)
    def test_same_semantic_water_alias_does_not_create_false_ambiguity(self):
        colors=[{'label':'W','terrain':None,'rgb':[68,78,88]} for _ in range(2)]
        self.assertEqual(self.image(colors,[[[68,78,88]]])[0][0,0],'W')
    def test_out_of_palette_color_is_not_forced_to_ground(self):
        self.assertEqual(self.image([{'label':'G','terrain':'Soil','rgb':[90,70,50]}],[[[94,70,50]]])[0][0,0],'N')
    def test_supported_and_constructed_same_color_is_rejected(self):
        colors=[{'label':'G','terrain':'Soil','rgb':[90,70,50]},{'label':'N','rgb':[90,70,50]}]
        self.assertEqual(self.image(colors,[[[90,70,50]]])[0][0,0],'N')
    def test_image_keeps_south_first_orientation_and_actual_ground_name(self):
        colors=[{'label':'G','terrain':'Soil','rgb':[96,78,63]},{'label':'G','terrain':'Sand','rgb':[126,112,93]}]
        _,names,_=self.image(colors,[[[96,78,63]],[[126,112,93]]])
        self.assertEqual(names[:,0].tolist(),['Sand','Soil'])
    def test_palette_reads_all_loaded_named_materials_and_records_missing_colors(self):
        native={'terrains':[terrain('Soil'),terrain('ModFertile',color=False),terrain('ThinIce',temporary=True,water=True)],'overlays':[]}
        p=image_palette(native);self.assertEqual(p['missing_preview_colors'],['ModFertile'])
        self.assertEqual(p['colors'][0]['terrain'],'Soil');self.assertEqual(p['colors'][1]['label'],'N')
    def test_default_mode_uses_its_own_colors_and_leaves_missing_unknown(self):
        soil=terrain('Soil');soil.update(default_rgb=[109,91,73],default_has_preview_color=True)
        native={'terrains':[soil,terrain('Sand')],'overlays':[]}
        p=image_palette(native,'default');self.assertEqual(p['colors'][0]['rgb'],[109,91,73])
        self.assertEqual(p['missing_preview_colors'],['Sand'])
    def test_direct_ground_can_use_named_def_even_without_preview_color(self):
        layer,_=ground_layer(np.full((4,4),'Soil'),np.full((4,4),'G'),{'terrains':[terrain('Soil',color=False)]},'TemperateForest')
        self.assertEqual(layer['materials'],[{'def':'Soil','role':'base'}])
        self.assertEqual(layer['runs'][0]['length'],16)
    def test_hazardous_material_not_imported_as_dry_ground(self):
        soil=terrain('Soil');soil['dangerous']=True
        layer,_=ground_layer(np.full((4,4),'Soil'),np.full((4,4),'G'),{'terrains':[soil]},'TemperateForest')
        self.assertEqual(layer['runs'],[])
    def test_ground_names_survive_and_water_rock_and_unknown_stay_zero(self):
        cells=np.array([list('GGMG'),list('GWSG')]);names=np.array([['Soil','SoilRich','Soil','Missing'],['Sand','Mud','Soil','BuiltFloor']])
        native={'terrains':[terrain('Soil'),terrain('SoilRich'),terrain('Sand'),terrain('BuiltFloor',supported=False)]}
        layer,receipt=ground_layer(names,cells,native,'TemperateForest',minimum=1)
        plane=np.zeros(8,dtype=int)
        for run in layer['runs']:plane[run['start']:run['start']+run['length']]=run['material']
        self.assertEqual(set(m['def'] for m in layer['materials']),{'Soil','SoilRich','Sand'})
        self.assertEqual(plane[[2,3,5,6,7]].tolist(),[0,0,0,0,0])
        self.assertEqual(receipt['ground_cells'],3)
    def test_ground_small_fragments_omitted_without_filling_unknown_holes(self):
        names=np.full((5,5),'Soil',dtype=object);names[2,2]='Missing';names[0,0]='SoilRich'
        cells=np.full((5,5),'G');native={'terrains':[terrain('Soil'),terrain('SoilRich')]}
        layer,receipt=ground_layer(names,cells,native,'TemperateForest',minimum=2)
        self.assertEqual(receipt['ground_cells'],23);self.assertEqual(len(layer['materials']),1)
    def test_histogram_only_capture_cannot_claim_ground_reconstruction(self):
        with tempfile.TemporaryDirectory() as root:
            path=pathlib.Path(root)/'terrain.json';path.write_text(json.dumps({'terrain_defs':{'Soil':4}}))
            with self.assertRaises(ValueError):observed_ground(path,np.full((2,2),'G'),{'terrains':[]})
    def test_per_cell_capture_overrides_same_histogram_different_layout(self):
        with tempfile.TemporaryDirectory() as root:
            path=pathlib.Path(root)/'terrain.json'
            data={'schema_version':2,'row_order':'south-first','biome':'TemperateForest','terrain_table':['Soil','Sand'],
                  'terrain_indices':[0]*12+[1]*12,'terrain_defs':{'Soil':12,'Sand':12}}
            path.write_text(json.dumps(data));native={'terrains':[terrain('Soil'),terrain('Sand')]}
            layer,_=observed_ground(path,np.full((4,6),'G'),native)
            self.assertEqual([(r['start'],r['length'],layer['materials'][r['material']-1]['def']) for r in layer['runs']],[(0,12,'Soil'),(12,12,'Sand')])
    def test_ground_recipe_not_promoted_without_ground_evidence(self):
        from finalize_catalog import finalize
        with tempfile.TemporaryDirectory() as root:
            root=pathlib.Path(root)
            (root/'catalog.json').write_text(json.dumps({'entries':[{'id':'dry','requires_ground_sidecar':True}]}))
            (root/'transfer-evaluation.json').write_text(json.dumps({'records':[{'run':'native','id':'dry','biome':'TemperateForest','size':250,'geometry_pass':True}]}))
            finalize(root);entry=json.loads((root/'catalog.json').read_text())['entries'][0]
            self.assertEqual(entry['status'],'prototype-quarantined');self.assertEqual(entry['verified_profiles'],[])
    def test_ground_failure_rejects_even_when_geometry_passes(self):
        from finalize_catalog import finalize
        with tempfile.TemporaryDirectory() as root:
            root=pathlib.Path(root)
            (root/'catalog.json').write_text(json.dumps({'entries':[{'id':'dry','requires_ground_sidecar':True}]}))
            (root/'transfer-evaluation.json').write_text(json.dumps({'records':[{'run':'native','id':'dry','biome':'TemperateForest','size':250,'geometry_pass':True}]}))
            evidence={'records':[{'run':'native','id':'dry','fidelity_pass':False}], 'application_checks':[{'run':'native','id':'dry','pass':True}]}
            (root/'ground-evaluation.json').write_text(json.dumps(evidence));finalize(root)
            self.assertEqual(json.loads((root/'catalog.json').read_text())['entries'][0]['verified_profiles'],[])
            evidence['records'][0]['fidelity_pass']=True
            (root/'ground-evaluation.json').write_text(json.dumps(evidence));finalize(root)
            self.assertEqual(len(json.loads((root/'catalog.json').read_text())['entries'][0]['verified_profiles']),1)

if __name__=='__main__':unittest.main(verbosity=2)
