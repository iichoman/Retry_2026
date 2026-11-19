using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PlayerPresentation
{
    public const int Packet = 206;
    public int version, entityId, team, action, serial;
    public float remaining, duration, dx, dz;
}
