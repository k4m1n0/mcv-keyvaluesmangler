// 打包格式 Stub.exe + Lamarr!! + [4B原始大小][4B压缩大小] + 压缩载荷
using System;
using System.IO;

namespace Lamarr;

public static class Packer
{
    private static readonly byte[] rgMagic = [0x4C, 0x61, 0x6D, 0x61, 0x72, 0x72, 0x21, 0x21];//Lamarr!!

    public static void Pack(byte[] rgStub, byte[] rgInput, string sOutputPath)
    {
        uint cbOriginal = (uint)rgInput.Length;
        uint cbOutCap = LamarrEncoder.GetMaxEncodedSize(cbOriginal);
        byte[] rgCompressed = new byte[cbOutCap];
        uint pcbOut = cbOutCap;

        int iResult = LamarrEncoder.Encode(rgCompressed, ref pcbOut, rgInput, cbOriginal);
        if (iResult != 0)
            throw new InvalidOperationException($"Lamarr encode failed: {iResult:X}");

        //压缩后写到新文件再覆盖原exe 避免读自己把自己写坏
        using var fs = new FileStream(sOutputPath, FileMode.Create, FileAccess.Write, FileShare.None);

        fs.Write(rgStub, 0, rgStub.Length);
        fs.Write(rgMagic, 0, rgMagic.Length);

        Span<byte> rgHeader = stackalloc byte[8];
        BitConverter.TryWriteBytes(rgHeader.Slice(0, 4), cbOriginal);
        BitConverter.TryWriteBytes(rgHeader.Slice(4, 4), pcbOut);
        fs.Write(rgHeader);

        fs.Write(rgCompressed, 0, (int)pcbOut);
        fs.Flush(true);
    }

    public static void Pack(string sStubPath, string sInputPath, string sOutputPath)
    {
        byte[] rgStub = File.ReadAllBytes(sStubPath);
        byte[] rgInput = File.ReadAllBytes(sInputPath);
        Pack(rgStub, rgInput, sOutputPath);
    }
}