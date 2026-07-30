using Core.SaveSystem;
using Features.Modules.CleaningModule.Dust;
using UnityEngine;

namespace Bootstrap
{
    /// <summary>
    /// Owns the order of operations for committing a save. UI asks for a save;
    /// this decides what a save consists of. Lives in App because it reaches across
    /// features - the same reason GameplayBootstrap does.
    /// </summary>
    public class SaveCoordinator:MonoBehaviour
    {
        public void Save()
        {
            CaptureCleanableSurfaces();
            WorldState.Save();
        }

        private static void CaptureCleanableSurfaces()
        {
            foreach (var surface in CleanableRegistry.All.Values)
                if (surface != null)
                    surface.CaptureMask();
        }
    }
}