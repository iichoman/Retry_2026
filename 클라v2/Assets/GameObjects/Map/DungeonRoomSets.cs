using System;
using UnityEngine;
[CreateAssetMenu(menuName="Retry/Dungeon Room Sets")]
public sealed class DungeonRoomSets:ScriptableObject {
 [Serializable] public class Set {public string label;public GameObject banner,feature;public bool moss;}
 public Set[] sets; public GameObject moss,largeBush,smallBush,hangingBush;
}
