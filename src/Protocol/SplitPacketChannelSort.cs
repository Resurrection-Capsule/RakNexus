using RakNexus.Core;

namespace RakNexus.Protocol;

public partial class SplitPacketChannel
{
    public void SortFragments()
    {
        var list = SplitPackets.Values.ToList();
        Quicksort(list, 0, list.Count - 1);
    }

    private void Quicksort(List<InternalPacket> data, int left, int right)
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
        if (left < j) Quicksort(data, left, j);
        if (i < right) Quicksort(data, i, right);
    }
}