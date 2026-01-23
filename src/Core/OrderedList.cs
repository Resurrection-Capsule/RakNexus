namespace RakNexus.Core;

public class OrderedList<TKey, TData> where TKey : IComparable<TKey>
{
    private readonly List<TData> _list = new();
    private readonly Func<TKey, TData, int> _comparator;

    public int Size => _list.Count;

    public OrderedList(Func<TKey, TData, int> comparator)
    {
        _comparator = comparator;
    }

    public int GetIndex(TKey key, out bool exists)
    {
        int low = 0, high = _list.Count - 1;
        exists = false;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            int res = _comparator(key, _list[mid]);
            if (res == 0) { exists = true; return mid; }
            if (res < 0) high = mid - 1;
            else low = mid + 1;
        }
        return low;
    }

    public void Insert(TKey key, TData data)
    {
        int index = GetIndex(key, out bool exists);
        if (!exists) _list.Insert(index, data);
    }

    public void RemoveAtIndex(int index) => _list.RemoveAt(index);
    public TData this[int index] => _list[index];
    public void Clear() => _list.Clear();
} 