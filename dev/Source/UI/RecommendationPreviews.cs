using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.Patches;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MapGenAI.UI
{
    // UI-thread owner; Map Preview runs one request at a time on its existing worker.
    // Discarding a batch lets an in-flight job finish, but cannot publish a late texture.
    public sealed class RecommendationPreviews : IDisposable
    {
        public sealed class Item
        {
            public Texture2D Texture;
            public string Error;
            public string Warning;
            public double Seconds;
            public bool Complete;
        }
        public readonly List<Item> Items = new List<Item>();
        readonly List<CandidatePreviewSnapshot> snapshots = new List<CandidatePreviewSnapshot>();
        readonly int tileId;
        readonly int seed;
        readonly IntVec2 mapSize;
        readonly World world;
        sealed class Pending
        {
            public readonly Item Item;
            public readonly int Index;
            public readonly float Since=Time.realtimeSinceStartup;
            public Pending(Item item,int index){Item=item;Index=index;}
        }
        const float RequestTimeoutSeconds=120f;
        bool disposed;
        Pending pending;
        int next;

        public RecommendationPreviews(int tileId, IReadOnlyList<RecommendationPlan> plans, TileMapState before)
        {
            this.tileId = tileId;
            world = Find.World;
            var size = world.info.initialMapSize;
            mapSize = new IntVec2(size.x, size.z);
            seed = MapPreviewIntegration.IsAvailable ? CurrentSeed(tileId) : 0;
            foreach (var plan in plans)
            {
                Items.Add(new Item());
                snapshots.Add(new CandidatePreviewSnapshot(tileId, plan.Resolve(before)));
            }
        }

        // Preserve the other textures. A previous in-flight version may finish but cannot publish.
        public void Replace(int index,RecommendationPlan plan,TileMapState before)
        {
            var snapshot=new CandidatePreviewSnapshot(tileId,plan.Resolve(before));
            var old=Items[index];
            if(old.Texture!=null)UnityEngine.Object.Destroy(old.Texture);
            Items[index]=new Item();snapshots[index]=snapshot;
            next=Math.Min(next,index);
        }

        public bool ContextMatches()
        {
            if (disposed || Find.World != world) return false;
            var size = world.info.initialMapSize;
            return size.x == mapSize.x && size.z == mapSize.z && (!MapPreviewIntegration.IsAvailable || CurrentSeed(tileId) == seed);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int CurrentSeed(int tile) => new MapPreview.MapPreviewRequest(Find.World.info.seedString, tile, new IntVec2(1, 1)).Seed;

        public void Update()
        {
            if (disposed) return;
            if(pending!=null)
            {
                if(Time.realtimeSinceStartup-pending.Since<RequestTimeoutSeconds)return;
                var expired=pending;pending=null;
                if(ReferenceEquals(expired.Item,Items[expired.Index]))
                {
                    expired.Item.Error=L10n.IsKorean()?"미리보기 응답 시간이 초과되었습니다. 다른 후보를 선택하거나 다시 추천받아 주세요.":
                        "Preview timed out. Choose another option or request new recommendations.";
                    expired.Item.Complete=true;
                }
                // A queued/running request may still finish. Keep its weak snapshot mapping
                // until that callback (or GC); removing it now would render the live map instead.
            }
            while(next<Items.Count && Items[next].Complete)next++;
            if(next>=Items.Count)return;
            if (!MapPreviewIntegration.IsAvailable)
            {
                foreach (var item in Items) { item.Error = L10n.IsKorean() ? "Map Preview 모드를 사용할 수 없습니다." : "Map Preview is unavailable."; item.Complete = true; }
                next = Items.Count; return;
            }
            QueueNext();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        void QueueNext()
        {
            if (!MapPreview.MapPreviewAPI.IsReady || MapGenerator.mapBeingGenerated != null) return;
            int index = next++;
            var item = Items[index];
            var snapshot=snapshots[index];
            var attempt=new Pending(item,index);pending=attempt;
            var request = new MapPreview.MapPreviewRequest(seed, tileId, mapSize)
            { UseMinimalMapComponents = true, UseTrueTerrainColors = true };
            // Closure fields must not reference optional assembly types: RimWorld enumerates
            // all types before Lunar loads MapPreview, including compiler-generated closures.
            object ticket = request;
            var timer = request.Timer;
            CandidatePreviewContext.Requests.Add(ticket, snapshot);
            try
            {
                MapPreview.MapPreviewGenerator.Init().QueuePreviewRequest(request).Then(result =>
                {
                    CandidatePreviewContext.Requests.Remove(ticket);
                    if(!ReferenceEquals(pending,attempt))return;
                    pending = null;
                    if (disposed || !ReferenceEquals(item,Items[index])) return;
                    Texture2D texture = null;
                    try
                    {
                        texture = new Texture2D(mapSize.x, mapSize.z) { filterMode = FilterMode.Point };
                        result.CopyToTexture(texture);
                        texture.Apply(false);
                        item.Texture = texture;
                        item.Seconds = timer.Elapsed.TotalSeconds;
                        item.Warning = snapshot.Report == null ? null : string.Join("\n", snapshot.Report.issues);
                        if(snapshot.Report?.nativeRoadPreviewLimited==true)item.Warning=(string.IsNullOrEmpty(item.Warning)?"":item.Warning+"\n")+RoadPlans.NativePreviewNote(L10n.IsKorean());
                    }
                    catch (Exception error) { if (texture != null) UnityEngine.Object.Destroy(texture); item.Error = error.Message; }
                    item.Complete = true;
                }).Catch(error =>
                {
                    CandidatePreviewContext.Requests.Remove(ticket);
                    if(!ReferenceEquals(pending,attempt))return;
                    pending = null;
                    if (!disposed) { item.Error = error.Message; item.Complete = true; }
                });
            }
            catch (Exception error)
            {
                CandidatePreviewContext.Requests.Remove(ticket);
                if(ReferenceEquals(pending,attempt))pending = null;
                item.Error = error.Message; item.Complete = true;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;pending=null;
            foreach (var item in Items)
            {
                if (item.Texture != null) UnityEngine.Object.Destroy(item.Texture);
                item.Texture = null;
            }
        }
    }
}
