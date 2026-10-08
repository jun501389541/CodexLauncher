using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CodexLauncher.App;

internal static class BridgeAdapterHardware
{
    internal static bool IsPhysical(string id)
    {
        if(!Guid.TryParse(id,out var guid))return false;
        var error=ConvertInterfaceGuidToLuid(ref guid,out var luid);
        if(error!=0)throw new Win32Exception(checked((int)error));
        var row=new InterfaceRow{InterfaceLuid=luid};
        error=GetIfEntry2(ref row);
        if(error!=0)throw new Win32Exception(checked((int)error));
        // Windows HardwareInterface / FilterInterface bits, not adapter-name heuristics.
        return row.InterfaceGuid==guid&&(row.InterfaceAndOperStatusFlags&3)==1;
    }

    [DllImport("iphlpapi.dll",ExactSpelling=true)]
    private static extern uint ConvertInterfaceGuidToLuid(ref Guid interfaceGuid,out ulong interfaceLuid);
    [DllImport("iphlpapi.dll",ExactSpelling=true)]
    private static extern uint GetIfEntry2(ref InterfaceRow row);

    // MIB_IF_ROW2 from netioapi.h; retain all trailing counters because Windows writes the full row.
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    internal struct InterfaceRow
    {
        internal ulong InterfaceLuid;
        internal uint InterfaceIndex;
        internal Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=257)] internal string Alias;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=257)] internal string Description;
        internal uint PhysicalAddressLength;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=32)] internal byte[] PhysicalAddress;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=32)] internal byte[] PermanentPhysicalAddress;
        internal uint Mtu,Type,TunnelType,MediaType,PhysicalMediumType,AccessType,DirectionType;
        internal byte InterfaceAndOperStatusFlags;
        internal uint OperStatus,AdminStatus,MediaConnectState;
        internal Guid NetworkGuid;
        internal uint ConnectionType;
        internal ulong TransmitLinkSpeed,ReceiveLinkSpeed,InOctets,InUcastPkts,InNUcastPkts,
            InDiscards,InErrors,InUnknownProtos,InUcastOctets,InMulticastOctets,InBroadcastOctets,
            OutOctets,OutUcastPkts,OutNUcastPkts,OutDiscards,OutErrors,OutUcastOctets,
            OutMulticastOctets,OutBroadcastOctets,OutQLen;
    }
}
