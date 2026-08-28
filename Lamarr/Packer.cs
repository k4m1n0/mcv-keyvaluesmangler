using System.Text;
using System.Text.Json;

namespace Lamarr;

public static class Packer
{
    private static readonly byte[] rgMagic = [0x4C, 0x61, 0x6D, 0x61, 0x72, 0x72, 0x21, 0x21];//Lamarr!!
    private static readonly byte[] rgBundleSig =
    [
        0x8B, 0x12, 0x02, 0xB9, 0x6A, 0x61, 0x20, 0x38, 0x72, 0x7B, 0x93, 0x02, 0x14, 0xD7, 0xA0, 0x32,
        0x13, 0xF5, 0xB9, 0xE6, 0xEF, 0xAE, 0x33, 0x18, 0xEE, 0x3B, 0x2D, 0xCE, 0x24, 0xB3, 0x6A, 0xAE,
    ];

    public static void Pack(string sStubPath, string sInputPath, string sOutputPath)
    {
        byte[] rgStub = File.ReadAllBytes(sStubPath);
        byte[] rgMain = File.ReadAllBytes(sInputPath);

        //从deps.json收集托管依赖 主dll同目录
        var rgDeps = CollectManagedDeps(sInputPath);

        int iCount = 1 + rgDeps.Count;
        var rgName = new string[iCount];
        var rgRaw = new byte[iCount][];
        rgName[0] = Path.GetFileName(sInputPath);
        rgRaw[0] = rgMain;
        for (int i = 0; i < rgDeps.Count; i++)
        {
            rgName[1 + i] = rgDeps[i].name;
            rgRaw[1 + i] = rgDeps[i].data;
        }

        //每条目独立压缩 依赖各自解压
        var rgBlocks = new byte[iCount][];
        var rgRawLen = new uint[iCount];
        var rgCompLen = new uint[iCount];
        var rgCompOff = new uint[iCount];
        uint uOrigTotal = 0, uDataOff = 0;
        for (int i = 0; i < iCount; i++)
        {
            rgRawLen[i] = (uint)rgRaw[i].Length;
            uOrigTotal += rgRawLen[i];
            uint uCap = LamarrEncoder.GetMaxEncodedSize(rgRawLen[i]);
            rgBlocks[i] = new byte[uCap];
            uint pcb = uCap;
            if (LamarrEncoder.Encode(rgBlocks[i], ref pcb, rgRaw[i], rgRawLen[i]) != 0)
                throw new InvalidOperationException($"Lamarr encode failed: {rgName[i]}");
            rgCompLen[i] = pcb;
            rgCompOff[i] = uDataOff;
            uDataOff += pcb;
        }

        //名字区4字节对齐
        var rgNameBytes = new byte[iCount][];
        uint uNameTotal = 0;
        for (int i = 0; i < iCount; i++)
        {
            rgNameBytes[i] = Encoding.UTF8.GetBytes(rgName[i]);
            uNameTotal += (uint)rgNameBytes[i].Length;
        }
        uint uNameArea = (uNameTotal + 3) & ~3u;

        //压缩后写到新文件再覆盖原exe 避免读自己把自己写坏
        using var fs = new FileStream(sOutputPath, FileMode.Create, FileAccess.Write, FileShare.None);

        //单文件stub的bundle header由hostfxr通过bundle marker的header_off定位
        //容器插在header之前会整体后移header 必须同步更新marker里的header_off指向新位置
        //否则hostfxr按旧偏移读到容器数据报"Bundle header version compatibility check failed"
        int iHeaderOff = FindBundleHeaderOff(rgStub);
        int iStubDataEnd = (iHeaderOff > 0 && iHeaderOff < rgStub.Length) ? iHeaderOff : rgStub.Length;
        if (iStubDataEnd < rgStub.Length)
        {
            int iContainerLen = 8 + 12 + iCount * 20 + (int)uNameArea + (int)uDataOff;
            int iMarker = FindBundleMarker(rgStub);
            if (iMarker > 0)
                Array.Copy(BitConverter.GetBytes((long)iHeaderOff + iContainerLen), 0, rgStub, iMarker, 8);
        }
        fs.Write(rgStub, 0, iStubDataEnd);
        fs.Write(rgMagic, 0, rgMagic.Length);

        fs.Write(BitConverter.GetBytes((uint)iCount));
        fs.Write(BitConverter.GetBytes(uOrigTotal));
        fs.Write(BitConverter.GetBytes(uDataOff));

        for (int i = 0; i < iCount; i++)
        {
            fs.Write(BitConverter.GetBytes((uint)rgNameBytes[i].Length));
            fs.Write(BitConverter.GetBytes(rgRawLen[i]));
            fs.Write(BitConverter.GetBytes(rgCompLen[i]));
            fs.Write(BitConverter.GetBytes(rgCompOff[i]));
            fs.Write(BitConverter.GetBytes(0u));
        }

        for (int i = 0; i < iCount; i++)
            fs.Write(rgNameBytes[i]);
        for (uint i = uNameTotal; i < uNameArea; i++)
            fs.WriteByte(0);

        for (int i = 0; i < iCount; i++)
            fs.Write(rgBlocks[i], 0, (int)rgCompLen[i]);

        //bundle header保持在文件尾
        if (iStubDataEnd < rgStub.Length)
            fs.Write(rgStub, iStubDataEnd, rgStub.Length - iStubDataEnd);

        fs.Flush(true);
    }

    //定位bundle header偏移 bundle marker = [header_off(8)][signature(32)]
    private static int FindBundleHeaderOff(byte[] rgData)
    {
        for (int i = 0; i + 8 + rgBundleSig.Length <= rgData.Length; i++)
        {
            if (rgData[i + 8] != rgBundleSig[0])
                continue;
            bool bOk = true;
            for (int j = 0; j < rgBundleSig.Length; j++)
            {
                if (rgData[i + 8 + j] != rgBundleSig[j])
                {
                    bOk = false;
                    break;
                }
            }
            if (bOk)
            {
                long lOff = BitConverter.ToInt64(rgData, i);
                if (lOff > 0 && lOff < rgData.Length)
                    return (int)lOff;
            }
        }
        return -1;
    }

    //返回bundle marker偏移([header_off(8)][signature(32)]的header_off所在位置)
    private static int FindBundleMarker(byte[] rgData)
    {
        for (int i = 0; i + 8 + rgBundleSig.Length <= rgData.Length; i++)
        {
            if (rgData[i + 8] != rgBundleSig[0])
                continue;
            bool bOk = true;
            for (int j = 0; j < rgBundleSig.Length; j++)
            {
                if (rgData[i + 8 + j] != rgBundleSig[j])
                {
                    bOk = false;
                    break;
                }
            }
            if (bOk)
            {
                long lOff = BitConverter.ToInt64(rgData, i);
                if (lOff > 0 && lOff < rgData.Length)
                    return i;
            }
        }
        return -1;
    }

    //解析主dll的deps.json收集runtime托管依赖 排除主程序集和卫星程序集
    private static List<(string name, byte[] data)> CollectManagedDeps(string sMainPath)
    {
        var rgDeps = new List<(string, byte[])>();
        string? sDepsJson = Path.ChangeExtension(sMainPath, ".deps.json");
        if (!File.Exists(sDepsJson)) return rgDeps;

        string sDir = Path.GetDirectoryName(sMainPath)!;
        string sMain = Path.GetFileName(sMainPath);
        using var doc = JsonDocument.Parse(File.ReadAllBytes(sDepsJson));
        if (!doc.RootElement.TryGetProperty("targets", out var rgTargets))
            return rgDeps;

        var rgSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tfm in rgTargets.EnumerateObject())
            foreach (var pkg in tfm.Value.EnumerateObject())
                foreach (var p in pkg.Value.EnumerateObject())
                {
                    //publish -r win-x64的deps.json用runtimeTargets(键带lib/net8.0/前缀) 普通publish用runtime
                    if (p.Name != "runtime" && p.Name != "runtimeTargets") continue;
                    foreach (var rt in p.Value.EnumerateObject())
                    {
                        string sDll = Path.GetFileName(rt.Name);
                        if (!sDll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                        if (sDll.Equals(sMain, StringComparison.OrdinalIgnoreCase)) continue;
                        if (sDll.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!rgSeen.Add(sDll)) continue;
                        string sPath = Path.Combine(sDir, sDll);
                        if (File.Exists(sPath))
                            rgDeps.Add((sDll, File.ReadAllBytes(sPath)));
                    }
                }
        return rgDeps;
    }
}
