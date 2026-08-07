// WeaponDamageCalc/Lamarr/StubLoader.cs
using System.Reflection;

namespace Lamarr;

internal static class StubLoader
{
    private static readonly byte[] rgMagic = [0x4C, 0x61, 0x6D, 0x61, 0x72, 0x72, 0x21, 0x21]; // "Lamarr!!"

    public static bool TryLoadFromTail(out Assembly asmPayload, out byte[] rgRawBytes)
    {
        asmPayload = null!;
        rgRawBytes = null!;

        string sSelfPath = Environment.ProcessPath!;
        Console.Error.WriteLine($"[StubLoader] Self: {sSelfPath}");

        byte[] rgSelf;
        try { rgSelf = File.ReadAllBytes(sSelfPath); }
        catch (Exception ex) { Console.Error.WriteLine($"[StubLoader] Read error: {ex.Message}"); return false; }

        Console.Error.WriteLine($"[StubLoader] Self size: {rgSelf.Length}");

        int iTail = FindMagic(rgSelf);
        Console.Error.WriteLine($"[StubLoader] FindMagic returned: {iTail}");
        if (iTail < 0) return false;

        int iDataStart = iTail + rgMagic.Length;
        Console.Error.WriteLine($"[StubLoader] Data start: {iDataStart}");

        if (iDataStart + 8 > rgSelf.Length) { Console.Error.WriteLine("[StubLoader] No room for header"); return false; }

        uint cbOriginal = BitConverter.ToUInt32(rgSelf, iDataStart);
        uint cbCompressed = BitConverter.ToUInt32(rgSelf, iDataStart + 4);
        iDataStart += 8;
        Console.Error.WriteLine($"[StubLoader] Header: orig={cbOriginal}, comp={cbCompressed}");

        if (cbOriginal == 0 || cbCompressed == 0) { Console.Error.WriteLine("[StubLoader] Header values are zero"); return false; }
        if (cbOriginal > 500_000_000 || cbCompressed > 500_000_000) { Console.Error.WriteLine("[StubLoader] Header values out of range"); return false; }
        if (cbCompressed >= cbOriginal) { Console.Error.WriteLine("[StubLoader] Compressed >= original"); return false; }
        if (iDataStart + cbCompressed > rgSelf.Length) { Console.Error.WriteLine($"[StubLoader] Data exceeds file: {iDataStart}+{cbCompressed} > {rgSelf.Length}"); return false; }

        Console.Error.WriteLine($"[StubLoader] Reading {cbCompressed} bytes of compressed data");
        byte[] rgCompressed = new byte[cbCompressed];
        Array.Copy(rgSelf, iDataStart, rgCompressed, 0, cbCompressed);

        byte[] rgDecompressed = new byte[cbOriginal];
        uint pcbOut = cbOriginal;
        Console.Error.WriteLine($"[StubLoader] Decoding...");
        Console.Error.WriteLine($"[StubLoader] Using Decoder version: UNSAVE");
        int iResult = LamarrDecoder.Decode(rgDecompressed, ref pcbOut, rgCompressed, cbCompressed);
        Console.Error.WriteLine($"[StubLoader] Decode result: {iResult}, output: {pcbOut}");
        if (iResult != 0) return false;

        rgRawBytes = rgDecompressed;
        Console.Error.WriteLine($"[StubLoader] Loading assembly...");
        asmPayload = Assembly.Load(rgDecompressed);
        Console.Error.WriteLine($"[StubLoader] Success!");
        return true;
    }

    private static int FindMagic(byte[] rgData)
    {
        int iLen = rgData.Length;
        int iMagicLen = rgMagic.Length;
        for (int i = iLen - iMagicLen; i >= 0; i--)
        {
            bool bMatch = true;
            for (int j = 0; j < iMagicLen; j++)
            {
                if (rgData[i + j] != rgMagic[j]) { bMatch = false; break; }
            }
            if (bMatch)
            {
                int iAfterMagic = i + iMagicLen;
                if (iAfterMagic + 8 <= iLen)
                {
                    uint cbOrig = BitConverter.ToUInt32(rgData, iAfterMagic);
                    uint cbComp = BitConverter.ToUInt32(rgData, iAfterMagic + 4);
                    if (cbOrig > 0 && cbComp > 0 && cbComp < cbOrig &&
                        iAfterMagic + 8 + cbComp <= iLen)
                        return i;
                }
            }
        }
        return -1;
    }
}