namespace VirtualTerminal.Interop;

internal struct COORD(ushort x, ushort y)
{
    public ushort X = x;
    public ushort Y = y;
}
