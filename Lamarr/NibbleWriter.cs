using System.Runtime.CompilerServices;

namespace Lamarr;

internal ref struct NibbleWriter
{
    private readonly byte[] _buf;
    private int _pos;
    private int _nib;

    public readonly int Pos => _pos;
    public readonly int Nib => _nib;

    public NibbleWriter(byte[] buf, int pos, int nib)
    {
        _buf = buf; _pos = pos; _nib = nib;
    }

    public void SetPos(int pos) => _pos = pos;
    public void SetNib(int nib) => _nib = nib;

    #region 位写入

    //U8
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(uint val)
    {
        if (_nib != 0)
        {
            _buf[_pos++] |= (byte)(val << 4);
            _buf[_pos]   = (byte)(val >> 4);
        }
        else
        {
            _buf[_pos++] = (byte)val;
        }
    }

    //U4
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteNibble(uint val)
    {
        _nib ^= 1;
        if (_nib == 1)
            _buf[_pos] = (byte)(val & 0xF);
        else
            _buf[_pos++] |= (byte)(val << 4);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLE12(uint val)
    {
        _nib ^= 1;
        if (_nib == 1)
        {
            _buf[_pos++] = (byte)val;
            _buf[_pos]   = (byte)(val >> 8);
        }
        else
        {
            _buf[_pos++] |= (byte)(val << 4);
            _buf[_pos++]  = (byte)(val >> 4);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLE16(uint val)
    {
        if (_nib != 0)
        {
            _buf[_pos++] |= (byte)(val << 4);
            _buf[_pos++]  = (byte)(val >> 4);
            _buf[_pos]    = (byte)(val >> 12);
        }
        else
        {
            _buf[_pos]   = (byte)val; _pos++;
            _buf[_pos]   = (byte)(val >> 8); _pos++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLE20(uint val)
    {
        _nib ^= 1;
        if (_nib == 1)
        {
            _buf[_pos]   = (byte)val; _pos++;
            _buf[_pos]   = (byte)(val >> 8); _pos++;
            _buf[_pos]   = (byte)(val >> 16);
        }
        else
        {
            _buf[_pos++] |= (byte)(val << 4);
            _buf[_pos]   = (byte)(val >> 4); _pos++;
            _buf[_pos]   = (byte)(val >> 12); _pos++;
        }
    }

    #endregion
}