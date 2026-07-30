using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;


namespace Core.SaveSystem
{
    
    /// <summary>
    /// wire format for the save file to avoid storing a unity type in
    /// a save file
    /// </summary>
    [Serializable]
    internal struct SaveVec3
    {
        public float x, y, z;
        public SaveVec3(Vector3 v) {x=v.x;y=v.y;z=v.z;}
        public Vector3 ToVector3() => new Vector3(x, y, z);
    }
    
    /// <summary>
    /// this is the class to store the data for saving. it can only be accessed through the
    /// it's mediator class that is currently named WorldState
    /// this is a separation of data and doing something with it
    /// </summary>
    internal class SaveData
    {
        public const int CurrentVersion = 1; //waht this build writes
        public int saveVersion = CurrentVersion; // what this file was written by
        
        //what character it is
        public string characterId;
        public string characterName;
        
        //character rantime state
        public Dictionary<string, float> attributeValues = new();
        
        //generic world state
        public Dictionary<string, bool> flags = new(); // something happened or not
        
        public Dictionary<string, int> counters = new(); // something happened or not
        
        public Dictionary<string, float> reactions = new(); //I need better name for this timers, floats
        
        public Dictionary<string, string> names = new(); // names
        
        public Dictionary<string, SaveVec3> positions = new(); // positions

        
        //saving textures in here so they go into .bin, not into .json
        [JsonIgnore] public Dictionary<string, byte[]> blobs = new();

        public float inGameTimeSeconds;
        
        //metadata
        public string savedAt;
    }
}