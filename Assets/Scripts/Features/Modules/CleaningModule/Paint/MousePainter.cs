using UnityEngine;
using UnityEngine.InputSystem;

namespace Paint
{
    public class MousePainter : MonoBehaviour
    {
        [SerializeField] private Camera cam;
        [SerializeField] private LayerMask surfaceMask = ~0;
        [SerializeField] private Brush brush; 
        [SerializeField] private Color color = Color.red;
        [SerializeField] private StrokeEmitter emitter = new();
        
        private PaintCanvas _current;

        private void Awake() { if (cam == null) cam = Camera.main; }

        private void Update()
        {
            if (!Mouse.current.leftButton.isPressed) { EndStroke(); return; }

            Ray ray =
                cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f,
                    surfaceMask)) { EndStroke(); return; }

            var canvas = hit.collider.GetComponentInParent<PaintCanvas>();   
            if (canvas == null) { EndStroke(); return; } // hit something non-paintable

            if (canvas != _current) { emitter.End(); _current = canvas; }    
            // crossed surfaces -> fresh stroke

            emitter.Extend(hit.point, color, brush, canvas);
        }

        private void EndStroke() { emitter.End(); _current = null; }

        // Hooks for the Scene-1 UI:
        public void SetColor(Color c) => color = c;
        public void SetBrush(Brush b) => brush = b;


    }
}