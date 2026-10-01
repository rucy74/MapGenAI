"""CPU text embeddings for the developer prototype, never loaded in Unity.

E5's model-card protocol: query:/passage:, attention-masked mean pooling, L2.
No paid API, remote Python, or trust_remote_code. Weights stay outside the repo.
"""
import argparse, json, pathlib, time
import numpy as np

MODEL='intfloat/multilingual-e5-small'
REVISION='614241f622f53c4eeff9890bdc4f31cfecc418b3'
CACHE=pathlib.Path('F:/Projects/Rimworld/work/mapgenai-library-model-cache')

class Embedder:
    def __init__(self, cache=CACHE):
        import torch
        from transformers import AutoModel, AutoTokenizer
        torch.set_num_threads(4)
        self.torch=torch
        self.tokenizer=AutoTokenizer.from_pretrained(MODEL,revision=REVISION,cache_dir=cache,token=False,trust_remote_code=False)
        self.model=AutoModel.from_pretrained(MODEL,revision=REVISION,cache_dir=cache,token=False,trust_remote_code=False,use_safetensors=True).eval()
        self.revision=self.model.config._commit_hash
    def encode(self,texts,kind):
        if kind not in ('query','passage'): raise ValueError('E5 task prefix required')
        vectors=[]
        for start in range(0,len(texts),8):
            tokens=self.tokenizer([kind+': '+x for x in texts[start:start+8]],padding=True,truncation=True,max_length=256,return_tensors='pt')
            with self.torch.inference_mode():
                hidden=self.model(**tokens).last_hidden_state
                mask=tokens['attention_mask'].unsqueeze(-1).bool()
                mean=hidden.masked_fill(~mask,0).sum(dim=1)/mask.sum(dim=1)
                vectors.append(self.torch.nn.functional.normalize(mean,p=2,dim=1).cpu().numpy())
        return np.concatenate(vectors).astype('float32') if vectors else np.empty((0,384),dtype='float32')
    def space(self):
        return {'model':MODEL,'revision':self.revision,'dimension':384,'pooling':'attention-masked-mean','normalization':'L2','document_prefix':'passage: ','query_prefix':'query: ','max_tokens':256}

def build(catalog,folder):
    entries=json.loads(catalog.read_text(encoding='utf-8'))['entries'];folder.mkdir(parents=True,exist_ok=True)
    clock=time.perf_counter();embedder=Embedder()
    vectors=embedder.encode([e['description_ko']+' '+e['description_en'] for e in entries],'passage')
    np.save(folder/'vectors.npy',vectors,allow_pickle=False)
    metadata={'space':embedder.space(),'entry_ids':[e['id'] for e in entries],'catalog_sha256':__import__('hashlib').sha256(catalog.read_bytes()).hexdigest(),'seconds':time.perf_counter()-clock,'paid_api_calls':0,'runtime':'developer Python CPU; not a game dependency'}
    (folder/'space.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(metadata,ensure_ascii=False))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--catalog',type=pathlib.Path);p.add_argument('--index',type=pathlib.Path);p.add_argument('--prepare',action='store_true')
    args=p.parse_args()
    if args.prepare:
        e=Embedder();print(json.dumps(e.space()))
    else: build(args.catalog,args.index)
