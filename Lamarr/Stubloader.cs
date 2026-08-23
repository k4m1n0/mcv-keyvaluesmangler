using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace Lamarr;

internal static class StubLoader
{
    private static readonly byte[] rgMagic = [0x4C, 0x61, 0x6D, 0x61, 0x72, 0x72, 0x21, 0x21];//Lamarr!!

    private readonly struct Entry
    {
        public readonly string Name;
        public readonly uint RawLen;
        public readonly uint CompLen;
        public readonly uint CompOff;
        public Entry(string sName, uint uRaw, uint uComp, uint uOff)
        { Name = sName; RawLen = uRaw; CompLen = uComp; CompOff = uOff; }
    }

    public static bool TryLoadFromTail(out Assembly asmPayload, out byte[] rgRawBytes)
    {
        asmPayload = null!;
        rgRawBytes = null!;

        //net5没有Environment.ProcessPath 从进程主模块取自身路径
        string sSelfPath = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("ProcessPath unavailable");
        byte[] rgSelf;
        try { rgSelf = File.ReadAllBytes(sSelfPath); }
        catch { return false; }

        //从文件末尾反搜 避免依赖固定偏移
        int iTail = FindMagic(rgSelf);
        if (iTail < 0) return false;

        int p = iTail + rgMagic.Length;
        if (p + 12 > rgSelf.Length) return false;
        int iCount = (int)BitConverter.ToUInt32(rgSelf, p); p += 4;
        uint uCompTotal = BitConverter.ToUInt32(rgSelf, p + 4);//压缩流总长 校验用
        if (iCount <= 0 || iCount > 0x1000) return false;

        long lTableEnd = p + 8 + (long)iCount * 20; // 条目表在 count+origTotal+compTotal 之后
        if (lTableEnd > rgSelf.Length) return false;
        var rgTable = new (uint nameLen, uint rawLen, uint compLen, uint compOff)[iCount];
        uint uNameTotal = 0;
        long lP = p + 8;
        for (int i = 0; i < iCount; i++)
        {
            rgTable[i] = (BitConverter.ToUInt32(rgSelf, (int)lP),
                          BitConverter.ToUInt32(rgSelf, (int)lP + 4),
                          BitConverter.ToUInt32(rgSelf, (int)lP + 8),
                          BitConverter.ToUInt32(rgSelf, (int)lP + 12));
            lP += 20;
            if (rgTable[i].rawLen == 0 || rgTable[i].compLen == 0) return false;
            uNameTotal += rgTable[i].nameLen;
        }
        uint uNameArea = (uNameTotal + 3) & ~3u;
        long lDataStart = lTableEnd + uNameArea;
        if (lDataStart + uCompTotal > rgSelf.Length) return false;

        var rgNames = new string[iCount];
        long lName = lTableEnd;
        for (int i = 0; i < iCount; i++)
        {
            rgNames[i] = Encoding.UTF8.GetString(rgSelf, (int)lName, (int)rgTable[i].nameLen);
            lName += rgTable[i].nameLen;
        }

        //主条目(条目0)整块解码并加载
        byte[]? rgMain = DecompressEntry(rgSelf, lDataStart, new Entry(rgNames[0], rgTable[0].rawLen, rgTable[0].compLen, rgTable[0].compOff));
        if (rgMain == null) return false;
        rgRawBytes = rgMain;
        asmPayload = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(rgMain));

        //依赖惰性加载 主程序集请求哪个解哪个
        var rgDeps = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < iCount; i++)
        {
            string s = rgNames[i];
            if (s.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                rgDeps[s[..^4]] = new Entry(s, rgTable[i].rawLen, rgTable[i].compLen, rgTable[i].compOff);
        }
        AssemblyLoadContext.Default.Resolving += (ctx, name) => LoadDependency(ctx, name, rgDeps, rgSelf, lDataStart);
        return true;
    }

    private static Assembly? LoadDependency(AssemblyLoadContext ctx, AssemblyName name,
        Dictionary<string, Entry> rgDeps, byte[] rgSelf, long lDataStart)
    {
        if (name.Name == null || !rgDeps.TryGetValue(name.Name, out var e))
            return null;
        byte[]? rgDep = DecompressEntry(rgSelf, lDataStart, e);
        if (rgDep == null) return null;
        try { return ctx.LoadFromStream(new MemoryStream(rgDep)); }
        finally { Array.Clear(rgDep, 0, rgDep.Length); }
    }

    private static byte[]? DecompressEntry(byte[] rgSelf, long lDataStart, Entry e)
    {
        byte[] rgComp = new byte[e.CompLen];
        Array.Copy(rgSelf, lDataStart + e.CompOff, rgComp, 0, e.CompLen);
        byte[] rgOut = new byte[e.RawLen];
        uint pcbOut = e.RawLen;
        int iResult = LamarrDecoder.Decode(rgOut, ref pcbOut, rgComp, e.CompLen);
        if (iResult != 0 || pcbOut != e.RawLen)
            return null;
        return rgOut;
    }

    //反搜同时验证头部有效性 跳过误匹配的假阳性
    private static int FindMagic(byte[] rgData)
    {
        int iLen = rgData.Length;
        for (int i = iLen - rgMagic.Length; i >= 0; i--)
        {
            bool bMatch = true;
            for (int j = 0; j < rgMagic.Length; j++)
            {
                if (rgData[i + j] != rgMagic[j]) { bMatch = false; break; }
            }
            if (bMatch)
            {
                int p = i + rgMagic.Length;
                if (p + 12 <= iLen)
                {
                    int iCount = (int)BitConverter.ToUInt32(rgData, p);
                    uint uOrig = BitConverter.ToUInt32(rgData, p + 4);
                    uint uComp = BitConverter.ToUInt32(rgData, p + 8);
                    if (iCount > 0 && iCount <= 0x1000 && uOrig > 0 && uComp > 0 &&
                        p + 12 + (long)iCount * 20 <= iLen &&
                        p + 12 + (long)iCount * 20 + uComp <= iLen)
                        return i;
                }
            }
        }
        return -1;
    }
}