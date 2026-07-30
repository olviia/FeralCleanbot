using System;

namespace Core.SaveSystem
{

    /// <summary>
    /// Marks a class whose public const string fields are fact keys, so the 
    /// editor key-picker can discover them by reflection. Pure metadata.    
    /// </summary>

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class FactKeySourceAttribute : Attribute { }

    
    /// <summary>
    /// single place in the whole project that created the keys
    /// </summary>
    [FactKeySource]
    public static class FactKeys
    {
        //there can be fixed keys that i write myself
        
        //and methods to build those keys

        public static string CutsceneFinished(string id) => $"cutscene.{id}.finished";
        public static string QuestStage(string id) => $"quest.{id}.stage";
        public static string QuestCompleted(string id) => $"quest.{id}.completed";
        public static string ModuleOwned(string id) => $"module.{id}.owned";
        
        public static string SurfaceMask(string id) => $"surface.{id}.mask";
        
        
        public const string TutorialPlayerMoved = "tutorial.moved";
        public const string TutorialPlayerRotated = "tutorial.rotated";
        public const string TutorialDoorOpened = "tutorial.door-opened";
    }
}