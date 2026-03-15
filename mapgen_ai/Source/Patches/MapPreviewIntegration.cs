using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MapGenAI.Patches
{
    /// <summary>
    /// Map Preview 모드 연동 (soft dependency).
    ///
    /// 중요: MapPreview 타입을 클래스 필드/상속에 절대 사용하지 않음.
    /// 메서드 내부 로컬 변수로만 사용해야 어셈블리 로딩 시 TypeLoadException 방지.
    /// </summary>
    public static class MapPreviewIntegration
    {
        private static readonly Dictionary<int, Texture2D> _cache = new Dictionary<int, Texture2D>();
        private static readonly HashSet<int> _pending = new HashSet<int>();

        private static bool _checkedAvailable = false;
        private static bool _available = false;

        public static bool IsAvailable
        {
            get
            {
                if (!_checkedAvailable)
                {
                    _checkedAvailable = true;
                    foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (asm.GetName().Name == "MapPreview")
                        {
                            _available = true;
                            break;
                        }
                    }
                }
                return _available;
            }
        }

        /// <summary>
        /// Map Preview로 생성된 텍스처를 rect에 그림.
        /// 아직 생성 중이면 false 반환 (fallback 필요).
        /// </summary>
        public static bool Draw(Rect rect, int tileId)
        {
            if (!IsAvailable) return false;

            // 캐시된 텍스처가 있으면 바로 그림
            if (_cache.TryGetValue(tileId, out var tex) && tex != null)
            {
                var prev = GUI.color;
                GUI.color = Color.white;
                GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, false);
                GUI.color = prev;
                return true;
            }

            // 아직 요청 안 했으면 요청
            if (!_pending.Contains(tileId))
                RequestPreview(tileId);

            return false; // 생성 중 — fallback 사용
        }

        /// <summary>
        /// MapPreview 타입은 이 메서드 내부에서만 사용.
        /// JIT 시점까지 MapPreview 타입 resolve 안 함.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void RequestPreview(int tileId)
        {
            _pending.Add(tileId);

            try
            {
                var gen = MapPreview.MapPreviewGenerator.Instance;
                if (gen == null)
                {
                    _pending.Remove(tileId);
                    return;
                }

                if (!MapPreview.MapPreviewAPI.IsReadyForPreviewGen)
                {
                    _pending.Remove(tileId);
                    return;
                }

                string seed = Find.World?.info?.seedString ?? "0";
                var worldMapSize = Find.World?.info?.initialMapSize ?? new IntVec3(250, 1, 250);
                var mapSize = new IntVec2(worldMapSize.x, worldMapSize.z);

                var req = new MapPreview.MapPreviewRequest(seed, tileId, mapSize)
                {
                    UseMinimalMapComponents = true,
                    UseTrueTerrainColors = true,
                };

                gen.QueuePreviewRequest(req).Then((MapPreview.MapPreviewResult result) =>
                {
                    try
                    {
                        var tex = new Texture2D(mapSize.x, mapSize.z);
                        result.CopyToTexture(tex);
                        tex.Apply(false);
                        _cache[tileId] = tex;
                    }
                    catch (System.Exception e)
                    {
                        Log.Error($"[MapGenAI] CopyToTexture 오류: {e.Message}");
                    }
                    finally
                    {
                        _pending.Remove(tileId);
                    }
                });

                Log.Message($"[MapGenAI] Map Preview 요청 (tileId={tileId})");
            }
            catch (System.Exception e)
            {
                Log.Error($"[MapGenAI] MapPreview 요청 실패: {e.Message}");
                _pending.Remove(tileId);
            }
        }

        public static void ClearCache()
        {
            _cache.Clear();
            _pending.Clear();
        }
    }
}
