namespace RakNexus.Core;

public static class Constants
{
    public const byte PROTOCOL_VERSION = 13;
    public const int DEFAULT_MTU = 1492;
    public const int UDP_HEADER_SIZE = 28;

    public static readonly byte[] MAGIC = new byte[] {
        0x00, 0xff, 0xff, 0x00, 0xfe, 0xfe, 0xfe, 0xfe,
        0xfd, 0xfd, 0xfd, 0xfd, 0x12, 0x34, 0x56, 0x78
    };
}
