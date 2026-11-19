/// <summary>Keep in sync with WorldSimulation::SpawnMonsters on the authoritative server.</summary>
public static class KnightEncounter
{
    public static bool IsKnightRoom(Room room) => room != null && room.type == RoomType.Reward;
}
