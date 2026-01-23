namespace RakNexus.Core;

public static class RakConstants
{
    public const string RAKNET_VERSION = "3.902";
    public const double RAKNET_VERSION_NUMBER = 3.902;
    public const byte RAKNET_PROTOCOL_VERSION = 13;
    public const int MAXIMUM_MTU_SIZE = 1492;
    public const int MAXIMUM_NUMBER_OF_INTERNAL_IDS = 10;
    
    public const ushort UNASSIGNED_PLAYER_INDEX = 65535;

    public const int UDP_HEADER_SIZE = 28;

    public static readonly byte[] OFFLINE_MESSAGE_DATA_ID = new byte[] {
        0x00, 0xFF, 0xFF, 0x00, 0xFE, 0xFE, 0xFE, 0xFE,
        0xFD, 0xFD, 0xFD, 0xFD, 0x12, 0x34, 0x56, 0x78
    };

    public static readonly uint[] ENGLISH_CHAR_FREQUENCIES = RakFrequencies.EnglishCharacterFrequencies;
}