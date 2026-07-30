using Core.SaveSystem;
using Paint;
using UnityEngine;

namespace Features.Modules.CleaningModule.Dust
{
    /// <summary>
    /// Gives one paintable surface a stable identity and makes it findable.
    /// Persistence and accounting for this surface will land here.
    /// Knows nothing about how the mask is stored (PaintCanvas) or how dust is
    /// drawn (DustSurface).
    /// </summary>
    [RequireComponent(typeof(PaintCanvas))]
    [DisallowMultipleComponent]
    public class CleanableSurface: MonoBehaviour
    {
        [SerializeField] private string id;
        [SerializeField] private PaintCanvas canvas;

        public string Id => id;
        public PaintCanvas Canvas => canvas;

        private string MaskKey => FactKeys.SurfaceMask(id);
        
        private void Reset() => canvas = GetComponent<PaintCanvas>();

        private void OnEnable()
        {
            if(canvas == null) canvas = GetComponent<PaintCanvas>();
            CleanableRegistry.Register(id, this);
            RestoreMask();
        }

        private void OnDisable() => CleanableRegistry.Unregister(id, this);

        /// <summary>Pulls this surface's mask out of the loaded save,
        /// if there is one.</summary>

        private void RestoreMask()
        {
            if(string.IsNullOrEmpty(id)) return;
            byte[] bytes = WorldState.GetBlob(MaskKey);
            if(bytes == null) return;
            
            canvas.WriteRaw(bytes);
        }

        /// <summary>Hands the current mask to the save.
        /// Called BY the save sequence, not by us.</summary>
        public void CaptureMask()
        {
            if(string.IsNullOrEmpty(id)) return;
            byte[] bytes = canvas.ReadRaw();
            if(bytes == null) return;
            WorldState.SetBlob(MaskKey, bytes);
        }
        
#if UNITY_EDITOR
        private void OnValidate()
        {
            if(canvas == null) canvas = GetComponent<PaintCanvas>();
            // deferred: OnValidate also runs mid-import, when the scene is not fully
            // loaded and a scene-wide search would lie to us
            UnityEditor.EditorApplication.delayCall += EnsureUniqueId;

        }     
        [ContextMenu("Debug: capture + save")]
        private void DebugCaptureAndSave()
        {
            CaptureMask();
            WorldState.Save();
        }

        private void EnsureUniqueId()
        {
            if (this == null) return;              
            if (!string.IsNullOrEmpty(id) && !IsIdTaken()) return;

            id = System.Guid.NewGuid().ToString("N");
            UnityEditor.EditorUtility.SetDirty(this);
        }
        private bool IsIdTaken()
        {
            foreach (var other in
                     FindObjectsByType<CleanableSurface>(FindObjectsSortMode.None))
                if (other != this && other.id == id) return true;
            return false;
        }
#endif

    }
}