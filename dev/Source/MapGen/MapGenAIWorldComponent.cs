using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace MapGenAI.MapGen
{
    /// <summary>
    /// 타일별 맵 생성 상태를 영구 저장하는 WorldComponent.
    /// 세이브/로드 시 유지됨. 각 타일의 TileMapState는 "현재 상태"이며 이력이 아님.
    /// </summary>
    public class MapGenAIWorldComponent : WorldComponent
    {
        private readonly object stateLock = new object();
        private Dictionary<int, TileMapState> tileStates = new Dictionary<int, TileMapState>();
        private Dictionary<int, TileWorldSnapshot> tileBaselines = new Dictionary<int, TileWorldSnapshot>();
        private Dictionary<int, TileWorldSnapshot> lastAppliedTiles = new Dictionary<int, TileWorldSnapshot>();

        public MapGenAIWorldComponent(World world) : base(world) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref tileStates, "tileStates", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref tileBaselines, "tileBaselines", LookMode.Value, LookMode.Deep);
            if (tileBaselines == null) tileBaselines = new Dictionary<int, TileWorldSnapshot>();
            Scribe_Collections.Look(ref lastAppliedTiles, "lastAppliedTiles", LookMode.Value, LookMode.Deep);
            if (lastAppliedTiles == null) lastAppliedTiles = new Dictionary<int, TileWorldSnapshot>();
            if (tileStates == null)
                tileStates = new Dictionary<int, TileMapState>();
        }

        /// <summary>타일의 현재 상태를 반환. 없으면 null.</summary>
        public TileMapState GetState(int tileId)
        {
            lock (stateLock) return tileStates.TryGetValue(tileId, out var state) ? state.Clone() : null;
        }

        /// <summary>타일의 현재 상태를 설정.</summary>
        public void SetState(int tileId, TileMapState state)
        {
            lock (stateLock) tileStates[tileId] = state.Clone();
        }

        public TileWorldSnapshot GetBaseline(int tileId) => tileBaselines.TryGetValue(tileId, out var value) ? value : null;
        public void SetBaseline(int tileId, TileWorldSnapshot baseline) => tileBaselines[tileId] = baseline;
        public void RemoveBaseline(int tileId) => tileBaselines.Remove(tileId);
        public TileWorldSnapshot GetLastApplied(int tileId) => lastAppliedTiles.TryGetValue(tileId, out var value) ? value : null;
        public void SetLastApplied(int tileId, TileWorldSnapshot snapshot) => lastAppliedTiles[tileId] = snapshot;
        public void RemoveLastApplied(int tileId) => lastAppliedTiles.Remove(tileId);

        /// <summary>타일의 상태를 삭제 (리셋).</summary>
        public void RemoveState(int tileId)
        {
            lock (stateLock) tileStates.Remove(tileId);
        }

        /// <summary>해당 타일에 상태가 있는지 확인.</summary>
        public bool HasState(int tileId)
        {
            lock (stateLock) return tileStates.ContainsKey(tileId);
        }

        /// <summary>현재 월드의 WorldComponent 인스턴스를 가져옴.</summary>
        public static MapGenAIWorldComponent Get()
        {
            if (Find.World == null) return null;
            return Find.World.GetComponent<MapGenAIWorldComponent>();
        }
    }
}
