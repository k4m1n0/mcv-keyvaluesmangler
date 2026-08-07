using System;
using System.Runtime.CompilerServices;

namespace Lamarr
{
    public class LamarrBitStream
    {
        private byte[] _rgBuf;
        private int _iPos;
        private int _iNib; // 0=低半字节, 1=高半字节

        public int iPos
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _iPos;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _iPos = value;
        }

        public int iNib
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _iNib;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _iNib = value;
        }

        public byte[] rgBuf
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _rgBuf;
        }

        public LamarrBitStream(byte[] rgBuf, int iPos, int iNib)
        {
            _rgBuf = rgBuf;
            _iPos = iPos;
            _iNib = iNib;
        }

        public LamarrBitStream(byte[] rgBuf) : this(rgBuf, 0, 0) { }

        // ---- 读取 ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetU4()
        {
            _iNib ^= 1;
            if (_iNib == 1)
                return (uint)(_rgBuf[_iPos] & 0xF);
            else
                return (uint)(_rgBuf[_iPos++] >> 4);
        }

        // 不推进 _iPos——调用方需手动 +1（对齐原版 LZMAT_GET_U8）
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetU8()
        {
            if (_iNib != 0)
                return (uint)((_rgBuf[_iPos] >> 4) | (_rgBuf[_iPos + 1] << 4));
            else
                return _rgBuf[_iPos++];
        }

        // 不推进 _iPos——调用方需手动推进（对齐原版 LZMAT_GET_LE12，_n_ 切换但不自动推进指针）
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetLE12()
        {
            _iNib ^= 1;
            if (_iNib == 1)
                return (uint)(((_rgBuf[_iPos + 1] & 0xF) << 8) | _rgBuf[_iPos]);
            else
            {
                uint uVal = (uint)((_rgBuf[_iPos] >> 4) | ((_rgBuf[_iPos + 1] & 0xF) << 4));
                _iPos++;
                return uVal;
            }
        }

        // 不推进 _iPos——调用方需手动推进（对齐原版 LZMAT_GET_LE16）
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetLE16()
        {
            if (_iNib != 0)
                return (uint)((_rgBuf[_iPos] >> 4) | ((GetLE16Native(_iPos + 1) & 0xFFF) << 4));
            else
                return GetLE16Native(_iPos);
        }

        // ---- 写入 ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetU4(uint uVal)
        {
            _iNib ^= 1;
            if (_iNib == 1)
                _rgBuf[_iPos] = (byte)(uVal & 0xF);
            else
                _rgBuf[_iPos++] |= (byte)(uVal << 4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetU8(uint uVal)
        {
            if (_iNib != 0)
            {
                _rgBuf[_iPos++] |= (byte)(uVal << 4);
                _rgBuf[_iPos] = (byte)(uVal >> 4);
            }
            else
            {
                _rgBuf[_iPos++] = (byte)uVal;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetLE12(uint uVal)
        {
            _iNib ^= 1;
            if (_iNib == 1)
            {
                _rgBuf[_iPos++] = (byte)uVal;
                _rgBuf[_iPos] = (byte)(uVal >> 8);
            }
            else
            {
                _rgBuf[_iPos++] |= (byte)(uVal << 4);
                _rgBuf[_iPos++] = (byte)(uVal >> 4);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetLE16(uint uVal)
        {
            if (_iNib != 0)
            {
                _rgBuf[_iPos++] |= (byte)(uVal << 4);
                _rgBuf[_iPos++] = (byte)(uVal >> 4);
                _rgBuf[_iPos] = (byte)(uVal >> 12);
            }
            else
            {
                SetLE16Native(_iPos, (ushort)uVal);
                _iPos += 2;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetLE20(uint uVal)
        {
            _iNib ^= 1;
            if (_iNib == 1)
            {
                SetLE16Native(_iPos, (ushort)uVal);
                _iPos += 2;
                _rgBuf[_iPos] = (byte)(uVal >> 16);
            }
            else
            {
                _rgBuf[_iPos++] |= (byte)(uVal << 4);
                SetLE16Native(_iPos, (ushort)(uVal >> 4));
                _iPos += 2;
            }
        }

        // ---- Unsave变体：读取时不推进半字节相位 ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetLE12Unsave()
        {
            if (_iNib != 0)
                return (uint)((GetLE16Native(_iPos) >> 4) & 0xFFF);
            else
                return (uint)(GetLE16Native(_iPos) & 0xFFF);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetLE16Unsave()
        {
            if (_iNib != 0)
                return (uint)((GetLE32Native(_iPos) >> 4) & 0xFFFF);
            else
                return GetLE16Native(_iPos);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetLE20Unsave()
        {
            if (_iNib != 0)
                return (uint)((GetLE32Native(_iPos) >> 4) & 0xFFFFF);
            else
                return GetLE32Native(_iPos) & 0xFFFFF;
        }

        // ---- 小端原生读写 ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ushort GetLE16Native(int iOff)
        {
            return (ushort)(_rgBuf[iOff] | (_rgBuf[iOff + 1] << 8));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint GetLE32Native(int iOff)
        {
            return (uint)(_rgBuf[iOff] | (_rgBuf[iOff + 1] << 8) |
                          (_rgBuf[iOff + 2] << 16) | (_rgBuf[iOff + 3] << 24));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetLE16Native(int iOff, ushort uVal)
        {
            _rgBuf[iOff] = (byte)uVal;
            _rgBuf[iOff + 1] = (byte)(uVal >> 8);
        }
    }
}