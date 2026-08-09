// Patcher/PeWriter.cs — PE assembly with native stub + compressed payload
using Lamarr;

namespace Patcher;

internal class PeWriter
{
    private byte[] _inputDll = null!;
    private byte[] _stubCode = null!;
    private byte[] _compPayload = null!;
    private uint _stubEntryRva;

    private int _peOff, _optOff;
    private ushort _origSecCount;
    private uint _sectAlign, _fileAlign;

    public void LoadDll(string sPath) => _inputDll = File.ReadAllBytes(sPath);

    public void LoadStub(string sPath)
    {
        byte[] rg = File.ReadAllBytes(sPath);
        int pe = BitConverter.ToInt32(rg, 0x3C);
        ushort ns = BitConverter.ToUInt16(rg, pe + 6);
        ushort os = BitConverter.ToUInt16(rg, pe + 20);
        int so = pe + 24 + os;

        uint va = 0, raw = 0, vs = 0;
        for (int i = 0; i < ns; i++)
        {
            int o = so + i * 40;
            if (BitConverter.ToUInt32(rg, o + 12) != 0) // skip BSS
            {
                va  = BitConverter.ToUInt32(rg, o + 12);
                raw = BitConverter.ToUInt32(rg, o + 20);
                vs  = BitConverter.ToUInt32(rg, o + 8);
                break;
            }
        }

        _stubCode = new byte[vs];
        Array.Copy(rg, (int)raw, _stubCode, 0, (int)Math.Min(vs, rg.Length - raw));

        // Find StubEntry: last push rbp; mov rbp,rsp pattern
        for (int i = _stubCode.Length - 4; i >= 0; i--)
        {
            if (_stubCode[i] == 0x55 && _stubCode[i+1] == 0x48 &&
                _stubCode[i+2] == 0x8B && _stubCode[i+3] == 0xEC)
            {
                _stubEntryRva = va + (uint)i;
                return;
            }
        }
        throw new InvalidOperationException("StubEntry not found in stub DLL");
    }

    public void Compress()
    {
        uint cb = (uint)_inputDll.Length;
        uint cap = LamarrEncoder.GetMaxEncodedSize(cb);
        byte[] buf = new byte[cap];
        uint n = cap;
        if (LamarrEncoder.Encode(buf, ref n, _inputDll, cb) != 0)
            throw new InvalidOperationException("Compression failed");
        _compPayload = new byte[n];
        Array.Copy(buf, 0, _compPayload, 0, n);
    }

    public void Write(string sOutPath)
    {
        _peOff  = BitConverter.ToInt32(_inputDll, 0x3C);
        _optOff = _peOff + 4 + 20;
        _origSecCount = BitConverter.ToUInt16(_inputDll, _peOff + 6);
        _sectAlign = BitConverter.ToUInt32(_inputDll, _optOff + 32);
        _fileAlign = BitConverter.ToUInt32(_inputDll, _optOff + 36);

        uint origHdrSize = BitConverter.ToUInt32(_inputDll, _optOff + 60);
        uint origImgSize = BitConverter.ToUInt32(_inputDll, _optOff + 56);
        ushort optSz = BitConverter.ToUInt16(_inputDll, _peOff + 20);
        int stdOff = _peOff + 24 + optSz;

        // Build import table for kernel32 (VirtualAlloc, VirtualProtect, ExitProcess)
        byte[] idata = BuildImportData();

        Console.WriteLine($"  StubEntry RVA: 0x{_stubEntryRva:X}  StubCode: {_stubCode.Length} bytes  ImportData: {idata.Length} bytes");

        // Layout: original content | .stub | .idata | .lzdata
        const int extSections = 3; // .stub, .idata, .lzdata
        uint newHdrSize = AlignUp(origHdrSize + (uint)(extSections * 40), _fileAlign);
        int hdrDelta = (int)(newHdrSize - origHdrSize);

        uint stubRaw  = AlignUp((uint)(_inputDll.Length + hdrDelta), _fileAlign);
        uint idatRaw  = AlignUp(stubRaw  + (uint)_stubCode.Length, _fileAlign);
        uint lzdRaw   = AlignUp(idatRaw  + (uint)idata.Length, _fileAlign);

        uint stubRva  = AlignUp(origImgSize, _sectAlign);
        uint idatRva  = AlignUp(stubRva  + (uint)_stubCode.Length, _sectAlign);
        uint lzdRva   = AlignUp(idatRva  + (uint)idata.Length, _sectAlign);
        uint newImg   = AlignUp(lzdRva   + (uint)_compPayload.Length, _sectAlign);

        // Fixup import data RVAs: relative to idatRva
        FixImportData(idata, idatRva);

        Console.WriteLine($"  newImg=0x{newImg:X} newHdrSize=0x{newHdrSize:X} origImgSize=0x{origImgSize:X}");

        using var fs = new FileStream(sOutPath, FileMode.Create);

        byte[] hdrs = new byte[newHdrSize];
        Array.Copy(_inputDll, 0, hdrs, 0, (int)origHdrSize);

        // Patch PE header fields
        BitConverter.GetBytes(_stubEntryRva).CopyTo(hdrs, _optOff + 16);
        BitConverter.GetBytes(newImg).CopyTo(hdrs, _optOff + 56);
        BitConverter.GetBytes(newHdrSize).CopyTo(hdrs, _optOff + 60);
        BitConverter.GetBytes((ushort)(_origSecCount + extSections)).CopyTo(hdrs, _peOff + 6);

        ushort characteristics = BitConverter.ToUInt16(hdrs, _peOff + 22);
        characteristics &= 0xDFFF;
        characteristics |= 0x0002;
        BitConverter.GetBytes(characteristics).CopyTo(hdrs, _peOff + 22);
        BitConverter.GetBytes((ushort)2).CopyTo(hdrs, _optOff + 68);

        // Patch import directory (data dir entry 1 = _optOff + 112 + 8)
        uint importRva = idatRva;
        BitConverter.GetBytes(importRva).CopyTo(hdrs, _optOff + 112 + 8);
        BitConverter.GetBytes((uint)idata.Length).CopyTo(hdrs, _optOff + 112 + 12);
        // IAT: reuse ILT array as IAT
        uint iatRva = idatRva + 54;
        BitConverter.GetBytes(iatRva).CopyTo(hdrs, _optOff + 112 + 96);
        BitConverter.GetBytes((uint)(4 * 8)).CopyTo(hdrs, _optOff + 112 + 100);

        // Shift original section raw pointers
        for (int i = 0; i < _origSecCount; i++)
        {
            int o = stdOff + i * 40;
            uint raw = BitConverter.ToUInt32(hdrs, o + 20);
            if (raw != 0) raw += (uint)hdrDelta;
            BitConverter.GetBytes(raw).CopyTo(hdrs, o + 20);
        }

        // Write new section entries
        int newOff = stdOff + _origSecCount * 40;
        WriteSection(hdrs, newOff,       ".stub",   stubRva, (uint)_stubCode.Length, stubRaw);
        WriteSection(hdrs, newOff + 40,  ".idata",  idatRva, (uint)idata.Length,     idatRaw);
        WriteSection(hdrs, newOff + 80,  ".lzdata", lzdRva,  (uint)_compPayload.Length, lzdRaw);

        fs.Write(hdrs, 0, hdrs.Length);
        fs.Write(_inputDll, (int)origHdrSize, _inputDll.Length - (int)origHdrSize);

        // .stub
        Pad(fs, (int)(stubRaw - newHdrSize - (_inputDll.Length - origHdrSize)));
        fs.Write(_stubCode, 0, _stubCode.Length);

        // .idata
        Pad(fs, (int)(idatRaw - stubRaw - _stubCode.Length));
        fs.Write(idata, 0, idata.Length);

        // .lzdata
        Pad(fs, (int)(lzdRaw - idatRaw - idata.Length));
        fs.Write(_compPayload, 0, _compPayload.Length);
    }

    static void WriteSection(byte[] hdrs, int off, string name, uint rva, uint vs, uint raw)
    {
        byte[] nb = System.Text.Encoding.ASCII.GetBytes(name.PadRight(8, '\0'));
        Array.Copy(nb, 0, hdrs, off, 8);
        BitConverter.GetBytes(vs).CopyTo(hdrs, off + 8);   // VirtualSize
        BitConverter.GetBytes(rva).CopyTo(hdrs, off + 12);  // VirtualAddress
        BitConverter.GetBytes(vs).CopyTo(hdrs, off + 16);   // SizeOfRawData
        BitConverter.GetBytes(raw).CopyTo(hdrs, off + 20);  // PointerToRawData
    }

    // Build import data with placeholder RVAs (will be fixed later).
    // Layout:
    //   [0]: IMAGE_IMPORT_DESCRIPTOR for kernel32 (20 bytes)
    //  [20]: Terminator IMAGE_IMPORT_DESCRIPTOR (20 bytes, zeroed)
    //  [40]: "kernel32.dll\0" (14 bytes)
    //  [54]: IAT: [RVA_VirtualAlloc] [RVA_VirtualProtect] [RVA_ExitProcess] [0] (32 bytes)
    //  [86]: Hint/Name for VirtualAlloc:  00 00 "VirtualAlloc\0"    (15 bytes)
    // [101]: Hint/Name for VirtualProtect: 00 00 "VirtualProtect\0" (17 bytes)
    // [118]: Hint/Name for ExitProcess:    00 00 "ExitProcess\0"    (14 bytes)
    static byte[] BuildImportData()
    {
        const int offDesc    = 0;     // IMAGE_IMPORT_DESCRIPTOR
        const int offDllName = 40;    // "kernel32.dll\0"
        const int offIAT     = 54;    // Import Address Table (4 entries * 8 bytes)
        const int offVAName  = 86;    // VirtualAlloc hint+name
        const int offVPName  = 101;   // VirtualProtect hint+name
        const int offEPName  = 118;   // ExitProcess hint+name
        const int totalSize  = 132;   // 86 + 15 + 17 + 14 = 132

        byte[] data = new byte[totalSize];

        // DESCRIPTOR: ILT and IAT both point to IAT array
        BitConverter.GetBytes((uint)offIAT).CopyTo(data, offDesc + 0);    // OriginalFirstThunk -> IAT
        BitConverter.GetBytes((uint)offDllName).CopyTo(data, offDesc + 12); // Name -> dll name
        BitConverter.GetBytes((uint)offIAT).CopyTo(data, offDesc + 16);   // FirstThunk -> IAT (same)

        // DLL name
        byte[] dllName = System.Text.Encoding.ASCII.GetBytes("kernel32.dll\0");
        Array.Copy(dllName, 0, data, offDllName, dllName.Length);

        // Import names
        byte[] vaName  = System.Text.Encoding.ASCII.GetBytes("\0\0VirtualAlloc\0");
        byte[] vpName  = System.Text.Encoding.ASCII.GetBytes("\0\0VirtualProtect\0");
        byte[] epName  = System.Text.Encoding.ASCII.GetBytes("\0\0ExitProcess\0");
        Array.Copy(vaName, 0, data, offVAName, vaName.Length);
        Array.Copy(vpName, 0, data, offVPName, vpName.Length);
        Array.Copy(epName, 0, data, offEPName, epName.Length);

        // IAT entries (relative offsets, will be fixed up later)
        BitConverter.GetBytes((uint)offVAName).CopyTo(data, offIAT);
        BitConverter.GetBytes((uint)offVPName).CopyTo(data, offIAT + 8);
        BitConverter.GetBytes((uint)offEPName).CopyTo(data, offIAT + 16);

        return data;
    }

    static void FixImportData(byte[] idata, uint baseRva)
    {
        AddRva(idata, 0, 0, baseRva);       // ILT (=IAT) RVA in descriptor
        AddRva(idata, 0, 12, baseRva);      // Name RVA in descriptor
        AddRva(idata, 0, 16, baseRva);      // IAT RVA in descriptor
        AddRva(idata, 54, 0, baseRva);      // IAT[0]
        AddRva(idata, 54, 8, baseRva);      // IAT[1]
        AddRva(idata, 54, 16, baseRva);     // IAT[2]
    }

    static void AddRva(byte[] data, int off, int sub, uint delta)
    {
        uint v = BitConverter.ToUInt32(data, off + sub);
        v += delta;
        BitConverter.GetBytes(v).CopyTo(data, off + sub);
    }

    static uint AlignUp(uint v, uint a) => (v + a - 1) & ~(a - 1);
    static void Pad(FileStream fs, int n) { while (n-- > 0) fs.WriteByte(0); }
}