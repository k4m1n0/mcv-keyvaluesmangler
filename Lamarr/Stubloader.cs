using System.Reflection;
using System.Runtime.Loader;

namespace Lamarr;

internal static class StubLoader
{
    private static readonly byte[] rgMagic = [0x4C, 0x61, 0x6D, 0x61, 0x72, 0x72, 0x21, 0x21];//Lamarr!!

    public static bool TryLoadFromTail(out Assembly asmPayload, out byte[] rgRawBytes)
    {
        asmPayload = null!;
        rgRawBytes = null!;

        string sSelfPath = Environment.ProcessPath!;

        byte[] rgSelf;
        try { rgSelf = File.ReadAllBytes(sSelfPath); }
        catch { return false; }

        //从文件末尾反搜 避免依赖固定偏移
        int iTail = FindMagic(rgSelf);
        if (iTail < 0) return false;

        int iDataStart = iTail + rgMagic.Length;
        if (iDataStart + 8 > rgSelf.Length) return false;

        uint cbOriginal = BitConverter.ToUInt32(rgSelf, iDataStart);
        uint cbCompressed = BitConverter.ToUInt32(rgSelf, iDataStart + 4);
        iDataStart += 8;

        if (cbOriginal == 0 || cbCompressed == 0) return false;
        if (cbOriginal > 500_000_000 || cbCompressed > 500_000_000) return false;
        if (cbCompressed >= cbOriginal) return false;
        if (iDataStart + cbCompressed > rgSelf.Length) return false;

        byte[] rgCompressed = new byte[cbCompressed];
        Array.Copy(rgSelf, iDataStart, rgCompressed, 0, cbCompressed);

        byte[] rgDecompressed = new byte[cbOriginal];
        uint pcbOut = cbOriginal;
        int iResult = LamarrDecoder.Decode(rgDecompressed, ref pcbOut, rgCompressed, cbCompressed);
        if (iResult != 0) return false;

        rgRawBytes = rgDecompressed;
        asmPayload = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(rgDecompressed));
        return true;
    }

    //反搜同时验证头部有效性 跳过误匹配的假阳性
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