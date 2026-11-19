using UnityEngine;

// Diagnostic identity only. Movement and damage are resolved by the server.
public sealed class DungeonPropObstacle : MonoBehaviour
{
    public int RoomId;
    public int GroupId;
    public DungeonPropLayout.Kind Kind;
    public bool Supported;
}
