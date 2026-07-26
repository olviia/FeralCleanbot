using UnityEngine;

namespace Paint
{
    // Leaves the same colour trail as MousePainter, but paints the spot directly
    // beneath the player as it moves. Identical settings — different ray source.
    public class PlayerPainter : MonoBehaviour
    {
        [SerializeField] private LayerMask surfaceMask = ~0;
        [SerializeField] private Color color = Color.red;
        [SerializeField] private Brush brush;
        [SerializeField] private StrokeEmitter emitter = new();

        [Header("Down-ray")]
        [SerializeField] private float castStart = 1f;   // begin this far above the pivot
        [SerializeField] private float castDepth = 5f;   // how far down to look for a surface

        [Header("Rainbow effect")]
        [SerializeField] private bool cycleHue = true;
        [SerializeField] private float hueSpeed = 0.2f;
        private float _hue;
        
        private PaintCanvas _current;

        private void Update()
        {
            Vector3 origin = transform.position + Vector3.up * castStart;    
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit,   
                    castStart + castDepth, surfaceMask))
            { EndStroke(); return; }

            var canvas = hit.collider.GetComponentInParent<PaintCanvas>();   
            if (canvas == null) { EndStroke(); return; }

            if (canvas != _current) { emitter.End(); _current = canvas; }    
            // crossed onto a new surface
            
            if (cycleHue)
            {
                _hue = Mathf.Repeat(_hue + hueSpeed * Time.deltaTime, 1f);
                color = Color.HSVToRGB(_hue, 0.9f, 1f);
            }


            emitter.Extend(hit.point, color, brush, canvas);
        }

        private void EndStroke() { emitter.End(); _current = null; }

        public void SetColor(Color c) => color = c;
        public void SetBrush(Brush b) => brush = b;
    }

}