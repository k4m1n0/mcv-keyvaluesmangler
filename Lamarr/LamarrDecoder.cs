using System.Runtime.CompilerServices;

namespace Lamarr;

public static class LamarrDecoder
{
    //编码阈值
    private const uint LZ_DEFAULT_CNT = 0x12;
    private const uint LZ_1BYTE_CNT  = 0xFF  + LZ_DEFAULT_CNT;
    private const uint LZ_2BYTE_CNT  = 0xFFFF + LZ_1BYTE_CNT;

    public static int Decode(byte[] dst, ref uint dstLen, byte[] src, uint srcLen)
    {
        uint outPos = 1;
        //首字节直接复制 格式要求
        dst[0] = src[0];

        var r = new NibbleReader(src, srcLen, pos: 1, nib: 0);

        //一个tag byte用8个bit管后续8个item bit=1是match bit=0是literal
        while (r.Pos < (srcLen - (uint)r.Nib))
        {
            uint tag = r.ReadByte();

            for (int bc = 0; bc < 8 && r.Pos < (srcLen - (uint)r.Nib) && outPos < dstLen; bc++, tag <<= 1)
            {
                if ((tag & 0x80) == 0)
                {
                    dst[outPos++] = (byte)r.ReadByte();
                    continue;
                }

                uint cflag = r.ReadLE20();
                uint dist;

                //outPos<0x881用短距离编码 少1bit分两个bucket
                if (outPos < 0x881)
                {
                    dist = cflag >> 1;
                    if ((cflag & 1) != 0)
                    {
                        r.SkipNibble();
                        dist = (dist & 0x7FF) + 0x81;
                    }
                    else
                    {
                        dist = (dist & 0x7F) + 1;
                    }
                }
                else
                {
                    dist = cflag >> 2;
                    switch (cflag & 3)
                    {
                        case 0: dist = (dist & 0x3F)   + 1;     break;
                        case 1: r.SkipNibble();     dist = (dist & 0x3FF) + 0x41;  break;
                        case 2: r.Advance(1);       dist = (dist & 0x3FFF) + 0x441; break;
                        case 3: r.SkipNibblePlusOne(); dist = (dist & 0x3FFFF) + 0x4441; break;
                    }
                }

                uint r_cnt = r.ReadLE12();
                r.SkipNibble();

                //4bit<15直接+3 得到3..17
                if ((r_cnt & 0xF) != 0xF)
                {
                    r_cnt = (r_cnt & 0xF) + 3;
                }
                else
                {
                    r.Advance(1);
                    //4bit=15 读1字节扩展
                    if (r_cnt != 0xFFF)
                    {
                        r_cnt = (r_cnt >> 4) + 0x12;
                    }
                    else
                    {
                        r_cnt = r.ReadLE16() + LZ_1BYTE_CNT;
                        r.Advance(2);

                        //哨兵值0x111+0xFFFF 触发非压缩块回退
                        if (r_cnt == LZ_2BYTE_CNT)
                        {
                            uint copyCnt;
                            if (r.Nib != 0)
                            {
                                copyCnt = ((uint)src[r.Pos - 4] & 0xFC) << 5;
                                r.Advance(1);
                                r.ClearNib();
                            }
                            else
                            {
                                copyCnt = (uint)((r.ReadU16At(r.Pos - 5) & 0xFC0) << 1);
                            }
                            copyCnt += (tag & 0x7F) + 4;
                            copyCnt <<= 1;
                            while (copyCnt-- > 0 && outPos < dstLen)
                            {
                                dst[outPos++] = src[r.Pos]; r.Advance(1);
                                dst[outPos++] = src[r.Pos]; r.Advance(1);
                                dst[outPos++] = src[r.Pos]; r.Advance(1);
                                dst[outPos++] = src[r.Pos]; r.Advance(1);
                            }
                            break;
                        }
                    }
                }

                if (outPos < dist) return 0x104;//距离越过输出起始 数据损坏
                if ((outPos + r_cnt) > dstLen) return 0x111;//输出缓冲区不够

                uint r_pos = outPos - dist;
                while (r_cnt-- > 0 && outPos < dstLen)
                    dst[outPos++] = dst[r_pos++];
            }
        }

        dstLen = outPos;
        return 0;
    }
}