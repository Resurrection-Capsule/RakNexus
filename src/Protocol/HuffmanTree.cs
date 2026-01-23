using RakNexus.Core;

namespace RakNexus.Protocol;

public class HuffmanEncodingTree
{
    private class Node : IComparable<Node>
    {
        public uint Weight;
        public short Value; // 0-255 or -1 for internal
        public Node? Left;
        public Node? Right;

        public bool IsLeaf => Left == null && Right == null;

        public int CompareTo(Node? other)
        {
            if (other == null) return 1;
            return Weight.CompareTo(other.Weight);
        }
    }

    private Node _root;
    private Dictionary<byte, (uint bits, int length)> _encodingTable = new();

    public HuffmanEncodingTree()
    {
        var pq = new PriorityQueue<Node, uint>();
        uint[] frequencies = RakFrequencies.EnglishCharacterFrequencies;

        for (int i = 0; i < 256; i++)
        {
            if (frequencies[i] > 0)
            {
                var node = new Node { Value = (short)i, Weight = frequencies[i] };
                pq.Enqueue(node, node.Weight);
            }
        }

        while (pq.Count > 1)
        {
            var left = pq.Dequeue();
            var right = pq.Dequeue();
            var parent = new Node
            {
                Value = -1,
                Weight = left.Weight + right.Weight,
                Left = left,
                Right = right
            };
            pq.Enqueue(parent, parent.Weight);
        }

        _root = pq.Dequeue();
        GenerateEncodingMap(_root, 0, 0);
    }

    private void GenerateEncodingMap(Node node, uint currentCode, int currentLength)
    {
        if (node.IsLeaf)
        {
            _encodingTable[(byte)node.Value] = (currentCode, currentLength);
            return;
        }

        // Left = 0
        if (node.Left != null) GenerateEncodingMap(node.Left, currentCode << 1, currentLength + 1);
        // Right = 1
        if (node.Right != null) GenerateEncodingMap(node.Right, (currentCode << 1) | 1, currentLength + 1);
    }

    public void Encode(string input, RakBitStream output)
    {
        if (input == null) return;
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(input);

        foreach (byte b in bytes)
        {
            if (_encodingTable.TryGetValue(b, out var path))
            {
                for (int i = path.length - 1; i >= 0; i--)
                {
                    output.Write(((path.bits >> i) & 1) == 1);
                }
            }
            else
            {
                // Should not happen for standard text in this implementation context
                // Fallback would be expensive, usually just assume mapped
            }
        }
    }

    public string Decode(RakBitStream input, int bitLength)
    {
        if (bitLength <= 0) return string.Empty;
        
        List<byte> decodedBytes = new List<byte>();
        int bitsProcessed = 0;
        
        while (bitsProcessed < bitLength)
        {
            Node current = _root;
            while (!current.IsLeaf)
            {
                if (bitsProcessed >= bitLength) break;
                
                bool bit;
                if (!input.Read(out bit)) break;
                
                current = bit ? current.Right! : current.Left!;
                bitsProcessed++;
            }
            decodedBytes.Add((byte)current.Value);
        }
        
        return System.Text.Encoding.ASCII.GetString(decodedBytes.ToArray());
    }
}