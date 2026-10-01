"""Read local GL authoring graphs. No workshop files or user settings are changed."""
import argparse, collections, hashlib, json, pathlib, xml.etree.ElementTree as ET

def inventory(root):
    result=[]
    for path in sorted(root.glob('Landform*.xml')):
        nodes=ET.parse(path).findall('./Nodes/Node')
        manifest=next(n for n in nodes if n.get('type')=='landformManifest')
        req=next((n for n in nodes if n.get('type')=='worldTileReq'),None)
        fields={n.get('name'): n.text for n in manifest}
        requirements={n.get('name'): ({c.tag:c.text for c in n} if len(n) else n.text) for n in req} if req is not None else {}
        result.append({'id':fields['Id'],'file':path.name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
                       'revision':fields.get('RevisionVersion'),'internal':fields.get('IsInternal')=='true',
                       'nodes':len(nodes),'node_types':dict(collections.Counter(n.get('type') for n in nodes)),
                       'requirements':requirements,'license':'CC-BY-NC-SA-4.0','author':'m00nl1ght',
                       'source':'https://github.com/m00nl1ght-dev/GeologicalLandforms'})
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',type=pathlib.Path,required=True);p.add_argument('--output',type=pathlib.Path,required=True)
    args=p.parse_args();items=inventory(args.root);args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps({'schema':1,'items':items},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'graphs':len(items),'ids':[i['id'] for i in items]},ensure_ascii=False))
