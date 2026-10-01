import json, pathlib, tempfile, unittest
import numpy as np
from PIL import Image
from contours import export, read_image
from retrieve import rank, filters, read_index
from skimage.measure import points_in_poly

def occupied(shape, point):
    """Independent polygon occupancy; subtraction follows the public core API."""
    values={s['id']:bool(points_in_poly(np.array([point]),np.array(s['verts']))[0]) for s in shape['shapes']}
    for op in shape['compose']:
        if op['op']=='sub':values[op['out']]=values[op['from']] and not values[op['a']]
        elif op['op']=='add':return values[op['s']]
    raise ValueError('No rendered operation')

def entry(ident,water=False,mountains=False,family=None,challenge=False):
    return {'id':ident,'family':family or ident,'command':'recipes/'+ident+'.json','source':{'kind':'synthetic fixture'},'status':'prototype-draft','challenge':challenge,
            'profiles':{'biomes':['TemperateForest'],'map_sizes':[250],'hilliness':['Flat']},'features':{'new_water':water,'new_mountains':mountains}}

class PrototypeTests(unittest.TestCase):
    def setUp(self):
        self.catalog={'entries':[entry('lake',water=True),entry('mountain',mountains=True),entry('dry'),entry('islands',water=True,challenge=True),entry('lake-copy',water=True,family='lake')]}
        self.vectors=np.array([[1,0,0],[.8,.6,0],[0,1,0],[.98,0,.2],[.99,.1,0]],dtype='float32')
        self.vectors/=np.linalg.norm(self.vectors,axis=1,keepdims=True)
        self.query={'text':'호숫가에서 살고 싶어','biome':'TemperateForest'}
    def selected(self,**changes):
        query={**self.query,**changes};rows,excluded,_=rank(self.catalog,self.vectors,np.array([1,0,0]),query)
        return [r['id'] for r in rows],excluded
    def test_semantic_order_uses_vector_not_file_order(self):
        ids,_=self.selected();self.assertEqual(ids[0],'lake')
    def test_water_forbidden_overrules_best_vector(self):
        ids,_=self.selected(text='물 없이 넓은 정착지');self.assertNotIn('lake',ids);self.assertNotIn('lake-copy',ids)
    def test_no_mount_en(self):
        ids,_=self.selected(text='without mountains near a lake');self.assertNotIn('mountain',ids)
    def test_native_water_excludes_disconnected_new_water(self):
        ids,_=self.selected(has_native_water=True);self.assertEqual(ids,[])
    def test_existing_world_road_profile_remains_with_existing_generator(self):
        self.assertEqual(self.selected(has_native_roads=True)[0],[])
    def test_existing_map_not_silently_replaced(self):
        self.assertEqual(self.selected(has_authored_map=True)[0],[])
    def test_partial_edit_skips_library(self):
        self.assertEqual(self.selected(scope='edit')[0],[])
    def test_unsupported_biome(self):
        self.assertEqual(self.selected(biome='IceSheet')[0],[])
    def test_unsupported_size(self):
        self.assertEqual(self.selected(map_size=100)[0],[])
    def test_unsupported_mountains_tile(self):
        self.assertEqual(self.selected(hilliness='Mountainous')[0],[])
    def test_exact_coverage_skips_unverified(self):
        self.assertEqual(self.selected(text='70%만 비옥한 토양')[0],[])
    def test_exact_position_skips_unverified(self):
        self.assertEqual(self.selected(text='왼쪽에 호수')[0],[])
    def test_diversity_one_per_family(self):
        ids,_=self.selected();self.assertFalse({'lake','lake-copy'}<=set(ids))
    def test_challenge_is_opt_in(self):
        self.assertNotIn('islands',self.selected()[0]);self.assertIn('islands',self.selected(text='군도나 여러 섬')[0])
    def test_recent_exclusion(self):
        self.assertNotIn('lake',self.selected(recent_ids=['lake'])[0])
    def test_failed_native_recipe_is_quarantined(self):
        self.catalog['entries'][0]['status']='prototype-quarantined'
        self.assertNotIn('lake',self.selected()[0])
    def test_verified_profile_is_exact_not_cartesian_product(self):
        self.catalog['entries'][0]['verified_profiles']=[{'biome':'TemperateForest','map_size':300,'hilliness':'Flat'}]
        self.assertNotIn('lake',self.selected(map_size=250)[0])
    def test_no_passing_profile_means_no_candidate(self):
        self.catalog['entries'][0]['verified_profiles']=[]
        self.assertNotIn('lake',self.selected()[0])
    def test_contour_export_has_hole_and_separate_depth(self):
        cells=np.full((100,100),'G');cells[10:90,10:90]='S';cells[20:80,20:80]='W';cells[35:65,35:65]='G'
        command,loss=export(cells,'island')
        parts=command['params']['shape_ops'];self.assertTrue(any(op['op']=='sub' for part in parts for op in part['shape']['compose']))
        fills=[x['shape']['compose'][-1].get('fill') for x in parts];self.assertEqual(fills,['WaterShallow','water'])
        shallow,deep=[p['shape'] for p in parts]
        self.assertTrue(occupied(shallow,[.15,.15]))
        self.assertFalse(occupied(deep,[.15,.15]))
        self.assertTrue(occupied(deep,[.25,.25]))
        self.assertFalse(occupied(shallow,[.5,.5]),'Island must survive the shallow footprint')
        self.assertFalse(occupied(deep,[.5,.5]),'Island must survive the deep core')
        self.assertFalse(occupied(shallow,[.95,.95]))
        self.assertNotIn('biome',command['params']);self.assertFalse(loss['procedural_reproduction'])
    def test_no_ground_or_bonus_import(self):
        cells=np.full((80,80),'G');cells[20:60,20:60]='M'
        command,_=export(cells,'mountain',water=False)
        self.assertEqual(set(command['params']),{'shape_ops'})
        self.assertTrue(all('fill' not in p['shape']['compose'][-1] for p in command['params']['shape_ops']))
    def test_empty_or_ambiguous_image_not_claimed_as_map(self):
        with tempfile.TemporaryDirectory() as root:
            root=pathlib.Path(root);Image.new('RGB',(100,100),(255,0,255)).save(root/'unknown.png')
            (root/'palette.json').write_text(json.dumps({'colors':[{'rgb':[0,100,130],'label':'W'}],'max_distance':10}))
            cells,receipt=read_image(root/'unknown.png',root/'palette.json')
            self.assertEqual(receipt['unknown_fraction'],1)
            with self.assertRaises(ValueError):export(cells,'unknown')
    def test_caches_detect_corruption_in_real_entrypoint(self):
        with tempfile.TemporaryDirectory() as root:
            root=pathlib.Path(root);catalog=root/'catalog.json';catalog.write_text(json.dumps({'entries':[entry('dry')]}))
            import hashlib
            meta={'catalog_sha256':hashlib.sha256(catalog.read_bytes()).hexdigest(),'entry_ids':['dry'],'space':{'dimension':3}}
            (root/'space.json').write_text(json.dumps(meta));np.save(root/'vectors.npy',np.array([[1,0,0]],dtype='float32'))
            self.assertEqual(read_index(catalog,root)[2].shape,(1,3))
            np.save(root/'vectors.npy',np.array([[float('nan'),0,0]],dtype='float32'))
            with self.assertRaises(ValueError):read_index(catalog,root)
            np.save(root/'vectors.npy',np.array([[1,0]],dtype='float32'))
            with self.assertRaises(ValueError):read_index(catalog,root)

if __name__=='__main__':unittest.main(verbosity=2)
