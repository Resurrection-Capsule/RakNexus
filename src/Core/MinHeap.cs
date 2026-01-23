namespace RakNexus.Core;

public class MinHeap<TWeight, TData> where TWeight : IComparable<TWeight>
{
    private struct Node
    {
        public TWeight Weight;
        public TData Data;
    }

    private readonly List<Node> _nodes = new();
    private bool _optimizeSeries = false;

    public int Size => _nodes.Count;

    public void StartSeries() => _optimizeSeries = false;

    public void Push(TWeight weight, TData data)
    {
        _nodes.Add(new Node { Weight = weight, Data = data });
        SiftUp(_nodes.Count - 1);
    }

    public void PushSeries(TWeight weight, TData data)
    {
        if (!_optimizeSeries)
        {
            if (_nodes.Count > 0 && weight.CompareTo(_nodes[(_nodes.Count - 1) / 2].Weight) < 0)
            {
                Push(weight, data);
                return;
            }
            _nodes.Add(new Node { Weight = weight, Data = data });
            _optimizeSeries = true;
        }
        else
        {
            _nodes.Add(new Node { Weight = weight, Data = data });
        }
    }

    public TData Pop()
    {
        if (_nodes.Count == 0) throw new InvalidOperationException("Empty heap");
        TData result = _nodes[0].Data;
        _nodes[0] = _nodes[^1];
        _nodes.RemoveAt(_nodes.Count - 1);
        if (_nodes.Count > 0) SiftDown(0);
        return result;
    }

    public TData Peek() => _nodes[0].Data;
    public TWeight PeekWeight() => _nodes[0].Weight;

    private void SiftUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (_nodes[index].Weight.CompareTo(_nodes[parent].Weight) >= 0) break;
            (_nodes[index], _nodes[parent]) = (_nodes[parent], _nodes[index]);
            index = parent;
        }
    }

    private void SiftDown(int index)
    {
        while (true)
        {
            int left = 2 * index + 1;
            int right = 2 * index + 2;
            int smallest = index;

            if (left < _nodes.Count && _nodes[left].Weight.CompareTo(_nodes[smallest].Weight) < 0) smallest = left;
            if (right < _nodes.Count && _nodes[right].Weight.CompareTo(_nodes[smallest].Weight) < 0) smallest = right;

            if (smallest == index) break;
            (_nodes[index], _nodes[smallest]) = (_nodes[smallest], _nodes[index]);
            index = smallest;
        }
    }

    public void Clear() => _nodes.Clear();
}