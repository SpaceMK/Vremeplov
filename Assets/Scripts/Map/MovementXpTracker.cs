using UnityEngine;

namespace TalesTensor.Map
{
    /// <summary>
    /// Awards the player experience for getting out and moving: 1 XP for every few
    /// seconds spent actually travelling (real GPS movement, or WASD in the editor).
    /// Reads the player marker's world position each frame from <see cref="MapController"/>,
    /// converts the per-frame delta to real-world speed, and only counts time when the
    /// player is moving faster than a small threshold (so GPS jitter doesn't grind XP).
    /// Created and driven by <see cref="MapController"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class MovementXpTracker : MonoBehaviour
    {
        // Below this real-world speed we treat the player as standing still.
        const float MinSpeedMetresPerSec = 0.5f;
        // Seconds of genuine movement that earn one experience point.
        const float SecondsPerXp = 3f;
        // Ignore absurd single-frame jumps (GPS teleports) so one glitch can't dump XP.
        const float MaxStepMetres = 60f;

        MapController _map;
        Vector3 _lastPos;
        bool _havePos;
        float _movingSeconds;

        public void Init(MapController map)
        {
            _map = map;
        }

        void Update()
        {
            if (_map == null || !_map.HasOrigin || _map.playerMarker == null) return;

            Vector3 pos = _map.playerMarker.position;
            if (!_havePos)
            {
                _lastPos = pos;
                _havePos = true;
                return;
            }

            float dt = Time.deltaTime;
            double metres = Vector3.Distance(pos, _lastPos) * _map.MetersPerUnit;
            _lastPos = pos;

            if (dt <= 0f || metres > MaxStepMetres) return;

            float speed = (float)(metres / dt);
            if (speed < MinSpeedMetresPerSec) return; // standing still — hold progress, don't reset

            _movingSeconds += dt;
            while (_movingSeconds >= SecondsPerXp)
            {
                _movingSeconds -= SecondsPerXp;
                PlayerProgress.Instance.AddXp(1);
            }
        }
    }
}
