// WeaponDamageCalc/Lamarr/Packer.cs
using System;
using System.IO;

namespace Lamarr;

public static class Packer
{
    private static readonly byte[] rgMagic = [0x4C, 0x61, 0x6D, 0x61, 0x72, 0x72, 0x21, 0x21]; // "Lamarr!!"

    public static void Pack(byte[] rgStub, byte[] rgInput, string sOutputPath)
    {
        uint cbOriginal = (uint)rgInput.Length;
        uint cbOutCap = LamarrEncoder.GetMaxEncodedSize(cbOriginal);
        byte[] rgCompressed = new byte[cbOutCap];
        uint pcbOut = cbOutCap;

        int iResult = LamarrEncoder.Encode(rgCompressed, ref pcbOut, rgInput, cbOriginal);
        if (iResult != 0)
            throw new InvalidOperationException($"Lamarr encode failed: {iResult:X}");

        using var fs = new FileStream(sOutputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        
        // 写入 stub
        fs.Write(rgStub, 0, rgStub.Length);
        
        // 写入魔数 "Lamarr!!"
        fs.Write(rgMagic, 0, rgMagic.Length);

        // 写入 8 字节头部：4字节原始大小 + 4字节压缩大小
        Span<byte> rgHeader = stackalloc byte[8];
        BitConverter.TryWriteBytes(rgHeader.Slice(0, 4), cbOriginal);
        BitConverter.TryWriteBytes(rgHeader.Slice(4, 4), pcbOut);
        fs.Write(rgHeader);

        // 写入压缩载荷
        fs.Write(rgCompressed, 0, (int)pcbOut);
        
        // 强制刷新到磁盘
        fs.Flush(true);
    }

    // 保留旧重载兼容
    public static void Pack(string sStubPath, string sInputPath, string sOutputPath)
    {
        byte[] rgStub = File.ReadAllBytes(sStubPath);
        byte[] rgInput = File.ReadAllBytes(sInputPath);
        Pack(rgStub, rgInput, sOutputPath);
    }
}