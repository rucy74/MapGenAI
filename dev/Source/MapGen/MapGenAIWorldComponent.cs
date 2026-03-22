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
        private Dictionary<int, TileMapState> tileStates = new Dictionary<int, TileMapState>();

        public MapGenAIWorldComponent(World world) : base(world) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref tileStates, "tileStates", LookMode.Value, LookMode.Deep);
            if (tileStates == null)
                tileStates = new Dictionary<int, TileMapState>();
        }

        /// <summary>타일의 현재 상태를 반환. 없으면 null.</summary>
        public TileMapState GetState(int tileId)
        {
            return tileStates.TryGetValue(tileId, out var state) ? state : null;
        }

        /// <summary>타일의 현재 상태를 설정.</summary>
        public void SetState(int tileId, TileMapState state)
        {
            tileStates[tileId] = state;
        }

        /// <summary>타일의 상태를 삭제 (리셋).</summary>
        public void RemoveState(int tileId)
        {
            tileStates.Remove(tileId);
        }

        /// <summary>해당 타일에 상태가 있는지 확인.</summary>
        public bool HasState(int tileId)
        {
            return tileStates.ContainsKey(tileId);
        }

        /// <summary>현재 월드의 WorldComponent 인스턴스를 가져옴.</summary>
        public static MapGenAIWorldComponent Get()
        {
            if (Find.World == null) return null;
            return Find.World.GetComponent<MapGenAIWorldComponent>();
        }
    }
}
