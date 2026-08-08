using System.Runtime.CompilerServices;

namespace Lamarr;

internal ref struct NibbleReader
{
    private readonly byte[] _buf;
    private readonly uint  _len;
    private int _pos;
    private int _nib;

    public readonly int Pos => _pos;
    public readonly int Nib => _nib;

    public NibbleReader(byte[] buf, uint len, int pos, int nib)
    {
        _buf = buf; _len = len; _pos = pos; _nib = nib;
    }

    #region 位读取

    //nibble对齐单字节
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadByte()
    {   
        uint val;
        if (_nib != 0)
            val = (uint)((_buf[_pos] >> 4) | (_buf[_pos + 1] << 4));
        else
            val = _buf[_pos];
        _pos++;
        return val;
    }

    //20bit LE 距离信息
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadLE20()
    {   
        uint val = _nib != 0
            ? (Read32At(_pos) >> 4) & 0xFFFFF
            : Read32At(_pos) & 0xFFFFF;
        _pos++;
        return val;
    }

    //12bit LE 匹配长度
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadLE12()
    {   
        return _nib != 0
            ? (uint)((Read16At(_pos) >> 4) & 0xFFF)
            : (uint)(Read16At(_pos) & 0xFFF);
    }

    //16bit LE 扩展长度
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadLE16()
    {
        return _nib != 0
            ? (uint)((Read32At(_pos) >> 4) & 0xFFFF)
            : Read16At(_pos);
    }

    #endregion
    #region 指针推进

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SkipNibble()        { _pos += _nib; _nib ^= 1; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SkipNibblePlusOne() { _pos += 1 + _nib; _nib ^= 1; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int delta)  { _pos += delta; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearNib()          { _nib = 0; }

    #endregion
    #region 安全底层读取

    //逐字节尽力读 缺位补0 允许部分越界
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly uint ReadU16At(int off)
    {
        uint v = 0;
        if (off < _len)       v  = _buf[off];
        if (off + 1 < _len)   v |= (uint)(_buf[off + 1] << 8);
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly uint Read16At(int off) => ReadU16At(off);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly uint Read32At(int off)
    {
        uint v = 0;
        if (off < _len)       v  = _buf[off];
        if (off + 1 < _len)   v |= (uint)(_buf[off + 1] << 8);
        if (off + 2 < _len)   v |= (uint)(_buf[off + 2] << 16);
        if (off + 3 < _len)   v |= (uint)(_buf[off + 3] << 24);
        return v;
    }

    #endregion
}