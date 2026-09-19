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
        bool disposed, pending;
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
                snapshots.Add(new CandidatePreviewSnapshot(tileId, MapStateEditor.Merge(before,
                    MapParameterParser.Parse(ProviderResponse.Command(plan.Command).GetObject("params")))));
            }
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
            if (disposed || pending || next >= Items.Count) return;
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
            pending = true;
            var request = new MapPreview.MapPreviewRequest(seed, tileId, mapSize)
            { UseMinimalMapComponents = true, UseTrueTerrainColors = true };
            // Closure fields must not reference optional assembly types: RimWorld enumerates
            // all types before Lunar loads MapPreview, including compiler-generated closures.
            object ticket = request;
            var timer = request.Timer;
            CandidatePreviewContext.Requests.Add(ticket, snapshots[index]);
            try
            {
                MapPreview.MapPreviewGenerator.Init().QueuePreviewRequest(request).Then(result =>
                {
                    CandidatePreviewContext.Requests.Remove(ticket);
                    pending = false;
                    if (disposed) return;
                    Texture2D texture = null;
                    try
                    {
                        texture = new Texture2D(mapSize.x, mapSize.z) { filterMode = FilterMode.Point };
                        result.CopyToTexture(texture);
                        texture.Apply(false);
                        item.Texture = texture;
                        item.Seconds = timer.Elapsed.TotalSeconds;
                        item.Warning = snapshots[index].Report == null ? null : string.Join("\n", snapshots[index].Report.issues);
                    }
                    catch (Exception error) { if (texture != null) UnityEngine.Object.Destroy(texture); item.Error = error.Message; }
                    item.Complete = true;
                }).Catch(error =>
                {
                    CandidatePreviewContext.Requests.Remove(ticket);
                    pending = false;
                    if (!disposed) { item.Error = error.Message; item.Complete = true; }
                });
            }
            catch (Exception error)
            {
                CandidatePreviewContext.Requests.Remove(ticket);
                pending = false; item.Error = error.Message; item.Complete = true;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var item in Items)
            {
                if (item.Texture != null) UnityEngine.Object.Destroy(item.Texture);
                item.Texture = null;
            }
        }
    }
}
