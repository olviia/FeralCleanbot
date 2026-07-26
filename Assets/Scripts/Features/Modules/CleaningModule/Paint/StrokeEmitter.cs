using UnityEngine;

namespace Paint
{
    // Turns a moving point into evenly spaced brush dabs (Photoshop-style spacing).
    // Framerate-independent: the leftover "carry" preserves dab phase across frames.
    [System.Serializable]
    public class StrokeEmitter
    {
        [Range(0.02f, 1f)] public float spacingFraction = 0.15f;  // gap as a fraction of brush diameter

        private Vector3 _last;
        private float   _carry;
        private bool    _active;

        public void Extend(Vector3 worldPos, Color color, in Brush brush, PaintCanvas canvas)
        {
            if (!_active)                                   // first contact:mark the start, arm the walk
            {
                _last = worldPos; _carry = 0f; _active = true;
                canvas.Stamp(worldPos, 0f, color, brush);
                return;
            }

            float spacing = Mathf.Max(1e-4f, brush.halfSize * 2f * spacingFraction);
            Vector3 delta = worldPos - _last;
            float segLen  = delta.magnitude;
            if (segLen < 1e-6f) return;                     // no movement this frame

            Vector3 dir = delta / segLen;
            float yaw   = Mathf.Atan2(dir.x, dir.z);        // each dab faces travel

            int budget = 4096;   // safety: don't melt on a teleport
            float d = Mathf.Max(0f, spacing - _carry);      // distance to the FIRST dab this segment
            for (; d <= segLen && budget-- > 0; d += spacing)
                canvas.Stamp(_last + dir * d, yaw, color, brush);

            _carry = segLen - (d - spacing);    // leftover -> next frame (stays in [0, spacing))
            _last  = worldPos;
        }

        public void End() => _active = false;
    }

}