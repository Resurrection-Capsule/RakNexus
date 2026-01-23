using RakNexus.Core;

namespace RakNexus.Protocol;

public class OrderingChannel
{
    public uint24 NextExpectedIndex = 0;
    public readonly SortedDictionary<uint24, InternalPacket> OutOfOrderBuffer = new();
    public uint24 NextSendIndex = 0;

    public void Push(InternalPacket packet)
    {
        Console.WriteLine($"[OrderingChannel.Push] Channel {packet.OrderingChannel}: Received OrderingIndex={packet.OrderingIndex}, expecting NextExpectedIndex={NextExpectedIndex}");
        
        if (packet.OrderingIndex < NextExpectedIndex) 
        {
            Console.WriteLine($"[OrderingChannel.Push] Discarding old packet (index {packet.OrderingIndex} < expected {NextExpectedIndex})");
            return;
        }
        
        OutOfOrderBuffer[packet.OrderingIndex] = packet;
        Console.WriteLine($"[OrderingChannel.Push] Buffered packet at index {packet.OrderingIndex}. Buffer size: {OutOfOrderBuffer.Count}");
    }

    public List<InternalPacket> PopInOrder()
    {
        Console.WriteLine($"[OrderingChannel.PopInOrder] Trying to pop packets. NextExpectedIndex={NextExpectedIndex}, Buffer size={OutOfOrderBuffer.Count}");
        
        var ready = new List<InternalPacket>();
        while (OutOfOrderBuffer.TryGetValue(NextExpectedIndex, out var packet))
        {
            Console.WriteLine($"[OrderingChannel.PopInOrder] Found packet at index {NextExpectedIndex}, delivering it");
            ready.Add(packet);
            OutOfOrderBuffer.Remove(NextExpectedIndex);
            NextExpectedIndex++;
        }
        
        if (ready.Count == 0 && OutOfOrderBuffer.Count > 0)
        {
            Console.WriteLine($"[OrderingChannel.PopInOrder] WARNING: No packets ready, but buffer has {OutOfOrderBuffer.Count} packets:");
            foreach (var kvp in OutOfOrderBuffer)
            {
                Console.WriteLine($"  - Buffer[{kvp.Key}] present");
            }
        }
        
        Console.WriteLine($"[OrderingChannel.PopInOrder] Returning {ready.Count} packets. New NextExpectedIndex={NextExpectedIndex}");
        return ready;
    }

    public uint24 GetNextSendOrderingIndex()
    {
        return NextSendIndex++;
    }
}