using System.Collections.Generic;
using UnityEngine;

namespace Features.Modules.CleaningModule.Dust
{
    /// <summary>
    /// Every cleanable surface currently alive. Entries add and remove themselves,
    /// so this can never hold a dead one. Runtime only - nothing here is saved.
    /// </summary>

    public static class CleanableRegistry
    {
        private static readonly Dictionary<string, CleanableSurface> surfaces = new();
        public static IReadOnlyDictionary<string, CleanableSurface> All => surfaces;

        public static void Register(string id, CleanableSurface surface)
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogError($"[Cleanable] {surface.name} has no id - it will never be saved");
                return;
            }

            if (surfaces.TryGetValue(id, out var existing) && existing != null && existing != surface)
            {
                Debug.LogError($"[Cleanable] id {id} claimed by both {existing.name} and {surface.name}");
                return;
            }
            
            surfaces[id] = surface;
            Debug.Log($"[Cleanable] registered '{id}' ({surface.name})");
        }
        
        public static void Unregister(string id, CleanableSurface surface)
        {
           if( surfaces.TryGetValue(id, out var current) && current == surface)
               surfaces.Remove(id);
        }
        public static bool TryGet(string id, out CleanableSurface surface) => surfaces.TryGetValue(id, out surface);
    }
}