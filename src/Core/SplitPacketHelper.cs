using RakNexus.Protocol;

namespace RakNexus.Core;

public static class SplitPacketHelper
{
    public static void SortFrames(List<InternalPacket> packets)
    {
        packets.Sort((a, b) => a.SplitPacketIndex.CompareTo(b.SplitPacketIndex));
    }
    public static void SortSplitPacketList(InternalPacket[] data, int left, int right)
    {
        if (left >= right) return;

        int i = left, j = right;
        uint pivot = data[(left + right) / 2].SplitPacketIndex;

        while (i <= j)
        {
            while (data[i].SplitPacketIndex < pivot) i++;
            while (data[j].SplitPacketIndex > pivot) j--;
            if (i <= j)
            {
                (data[i], data[j]) = (data[j], data[i]);
                i++; j--;
            }
        }

        if (left < j) SortSplitPacketList(data, left, j);
        if (i < right) SortSplitPacketList(data, i, right);
    }
}