namespace Lamarr.Packer;
using Lamarr;
using System.Text;
using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Numerics;

internal partial class PeWriterAntheil
{
    //等长且行为不变的指纹随机化 每产物随机 跳过栈指针 全表含32位与64位互换 保守表仅异或与减法
    static void ScrambleFingerprint(byte[] rgB, Random rng, bool bConservative, int iStart = 0, int iEnd = -1)
    {
        int iLim = iEnd < 0 ? rgB.Length : Math.Min(iEnd, rgB.Length);
        for (int i = Math.Max(0, iStart); i + 2 < iLim; i++)
        {
            byte b0 = rgB[i], b1 = rgB[i + 1];
            bool fRex = b0 == 0x48;
            if (!fRex && (b0 == 0x33 || b0 == 0x29) && (b1 & 0xC0) == 0xC0 && (b1 & 7) != 4
                && (b1 >> 3) == (b1 & 7))//三三与二九只在同一个寄存器上等价(自异或与自减都清零) 两个寄存器不同就换了语义
            {
                if (rng.Next(2) == 0) rgB[i] = (byte)(b0 ^ 0x1A);
                i++;
                continue;
            }
            if (fRex && (b1 == 0x33 || b1 == 0x29) && (rgB[i + 2] & 0xC0) == 0xC0 && (rgB[i + 2] & 7) != 4
                && (rgB[i + 2] >> 3) == (rgB[i + 2] & 7) && (b0 & 4) == (b0 & 1))//REX.R 与 REX.B 选出的那一对也要同号
            {
                if (rng.Next(2) == 0) rgB[i + 1] = (byte)(b1 ^ 0x1A);
                i += 2;
                continue;
            }
            if (bConservative) continue;
            if (!fRex && b0 == 0x83 && (b1 & 0xC0) == 0xC0 && (b1 & 7) != 4)
            {
                int iReg = (b1 >> 3) & 7;
                if (iReg == 0 || iReg == 5)
                {
                    int iImm = i + 2;
                    if (iImm < iLim && rgB[iImm] != 0x80)
                    {
                        if (rng.Next(2) == 0)
                        {
                            rgB[iImm] = (byte)(-(sbyte)rgB[iImm]);
                            rgB[i + 1] = (byte)(((uint)(b1) & 0xC7u) | ((uint)(iReg == 0 ? 5 : 0) << 3));
                        }
                        i += 2;
                        continue;
                    }
                }
            }
            if (fRex && b1 == 0x83 && (rgB[i + 2] & 0xC0) == 0xC0 && (rgB[i + 2] & 7) != 4)
            {
                int iReg = (rgB[i + 2] >> 3) & 7;
                if (iReg == 0 || iReg == 5)
                {
                    int iImm = i + 3;
                    if (iImm < iLim && rgB[iImm] != 0x80)
                    {
                        if (rng.Next(2) == 0)
                        {
                            rgB[iImm] = (byte)(-(sbyte)rgB[iImm]);
                            rgB[i + 2] = (byte)(((uint)(rgB[i + 2]) & 0xC7u) | ((uint)(iReg == 0 ? 5 : 0) << 3));
                        }
                        i += 3;
                        continue;
                    }
                }
            }
            if (!fRex && b0 == 0x81 && (b1 & 0xC0) == 0xC0 && (b1 & 7) != 4)
            {
                int iReg = (b1 >> 3) & 7;
                if (iReg == 0 || iReg == 5)
                {
                    int iImm = i + 2;
                    if (iImm + 3 < iLim)
                    {
                        if (rng.Next(2) == 0)
                        {
                            uint uV = BitConverter.ToUInt32(rgB, iImm);
                            BitConverter.GetBytes(0u - uV).CopyTo(rgB, iImm);
                            rgB[i + 1] = (byte)(((uint)(b1) & 0xC7u) | ((uint)(iReg == 0 ? 5 : 0) << 3));
                        }
                        i += 5;
                        continue;
                    }
                }
            }
            if (fRex && b1 == 0x81 && (rgB[i + 2] & 0xC0) == 0xC0 && (rgB[i + 2] & 7) != 4)
            {
                int iReg = (rgB[i + 2] >> 3) & 7;
                if (iReg == 0 || iReg == 5)
                {
                    int iImm = i + 3;
                    if (iImm + 3 < iLim)
                    {
                        if (rng.Next(2) == 0)
                        {
                            uint uV = BitConverter.ToUInt32(rgB, iImm);
                            BitConverter.GetBytes(0u - uV).CopyTo(rgB, iImm);
                            rgB[i + 2] = (byte)(((uint)(rgB[i + 2]) & 0xC7u) | ((uint)(iReg == 0 ? 5 : 0) << 3));
                        }
                        i += 6;
                        continue;
                    }
                }
            }
        }
    }

    //程序集文件等长且行为不变的扰动 仅限代码节
    static void ScrambleTextSection(byte[] rgDll, Random rng, bool bConservative)
    {
        if (rgDll == null || rgDll.Length < 0x80) return;
        int iPe = BitConverter.ToInt32(rgDll, 0x3C);
        if (iPe + 0x18 > rgDll.Length || BitConverter.ToUInt32(rgDll, iPe) != 0x4550) return;
        ushort usNum = BitConverter.ToUInt16(rgDll, iPe + 6);
        ushort usOpt = BitConverter.ToUInt16(rgDll, iPe + 20);
        int iSec = iPe + 4 + 20 + usOpt;
        for (int i = 0; i < usNum; i++)
        {
            int o = iSec + i * 40;
            if (o + 40 > rgDll.Length) break;
            string sName = Encoding.ASCII.GetString(rgDll, o, 8).TrimEnd('\0');
            if (sName != ".text") continue;
            uint uRaw = BitConverter.ToUInt32(rgDll, o + 20);
            uint uRsz = BitConverter.ToUInt32(rgDll, o + 16);
            int iStart = (int)uRaw;
            int iEnd = (int)Math.Min((uint)rgDll.Length, uRaw + uRsz);
            if (iStart < 0 || iEnd <= iStart + 2) return;
            ScrambleFingerprint(rgDll, rng, bConservative, iStart, iEnd);
            return;
        }
    }
}
