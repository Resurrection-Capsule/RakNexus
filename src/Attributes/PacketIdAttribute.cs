using System;
using RakNexus.Protocol;

namespace RakNexus.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class PacketIdAttribute : Attribute
{
    public MessageId ID { get; }
    public PacketIdAttribute(MessageId id)
    {
        ID = id;
    }
}