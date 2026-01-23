using System;
using RakNexus.Protocol;

namespace RakNexus.Core;

public static class RakString
{
    public static void Serialize(string? str, RakBitStream bs)
    {
        str ??= string.Empty;
        ushort len = (ushort)str.Length;
        bs.Write(len);
        if (len > 0)
        {
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes(str);
            bs.WriteBits(bytes, len * 8, true);
        }
    }

    public static string Deserialize(RakBitStream bs)
    {
        ushort len;
        if (!bs.Read(out len)) return string.Empty;
        if (len == 0) return string.Empty;

        byte[] bytes = new byte[len];
        if (bs.ReadBits(bytes, len * 8, true))
        {
            return System.Text.Encoding.ASCII.GetString(bytes);
        }
        return string.Empty;
    }
    
    public static void SerializeCompressed(string? str, RakBitStream bs, int languageId = 0, bool writeLanguageId = false)
    {
        if (writeLanguageId)
            bs.WriteCompressed((uint)languageId);
        
        StringCompressor.Instance.WriteCompressed(bs, str);
    }

    public static string DeserializeCompressed(RakBitStream bs, bool readLanguageId = false)
    {
        if (readLanguageId)
        {
            bs.ReadCompressed(out uint _);
        }
        
        StringCompressor.Instance.ReadCompressed(bs, out string result);
        return result;
    }
}