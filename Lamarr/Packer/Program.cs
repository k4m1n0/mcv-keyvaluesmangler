using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lamarr.Packer;
internal static class Program
{
    static readonly Random s_rng = new();//net5没有Random.Shared也没有无参NextInt64 打包器单线程无所谓了

    static string? s_sCfgPath;
    static readonly Dictionary<string, string> s_mpCfg = new(StringComparer.Ordinal);

    const int iOk = 0;
    const int iErrUsage = 1;
    const int iErrCheck = 2;
    const int iErrUnavailable = 3;
    const int iErrUnknownCmd = 4;
    const int iErrException = 5;

    static int Main(string[] rgArgs)
    {
        if (rgArgs.Length == 0)
        {
            ShowHelp();
            return iErrUsage;
        }
        if (HasFlag(rgArgs, "--help") || HasFlag(rgArgs, "-h") || HasFlag(rgArgs, "/?"))
        {
            ShowHelp();
            return iOk;
        }
        if (HasFlag(rgArgs, "--version"))
        {
            ShowVersion();
            return iOk;
        }
        string? sWorkDir = Opt(rgArgs, "-D");//相对路径与自动发现的基准
        if (sWorkDir != null)
        {
            if (!Directory.Exists(sWorkDir)) { Console.Error.WriteLine($"Work dir not found: {sWorkDir}"); return iErrUsage; }
            Directory.SetCurrentDirectory(sWorkDir);
        }
        LoadConfig(rgArgs);
        string? sCmd = rgArgs[0].StartsWith("-") ? null : rgArgs[0];//首个裸参数即命令
        if (sCmd != null && !IsMode(sCmd))
        {
            Console.Error.WriteLine($"Unknown command: {sCmd}");
            ShowHelp();
            return iErrUnknownCmd;
        }
        string? sStub = Opt(rgArgs, "--stub") ?? Cfg("LamarrStub");
        string? sInput = Opt(rgArgs, "--input") ?? Cfg("LamarrInput");
        string? sOutput = Opt(rgArgs, "--output") ?? Cfg("LamarrOutput");
        string? sBoot = Opt(rgArgs, "--boot") ?? Cfg("LamarrBoot");
#if LAMARR_ANTHEIL
        string? sShim = Opt(rgArgs, "--shim") ?? Cfg("ShimDll");
        string? sDecoder = Opt(rgArgs, "--decoder") ?? Cfg("LamarrDecoder");
        string? sJitHook = Opt(rgArgs, "--jithook") ?? Cfg("LamarrJitHook");
        string? sPheropod = Opt(rgArgs, "--pheropod") ?? Cfg("LamarrPheropod");
        string? sEngine = Opt(rgArgs, "--engine") ?? Cfg("EngineDll");
#endif
        string sMode = Opt(rgArgs, "--mode") ?? sCmd ?? Cfg("Packer") ?? "clean";
#if !LAMARR_ANTHEIL
        if (!sMode.Equals("managed", StringComparison.OrdinalIgnoreCase)
            && !sMode.Equals("native", StringComparison.OrdinalIgnoreCase)
            && !sMode.Equals("clean", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Mode not in this build: {sMode} (available: {Modes()})");
            return iErrUnavailable;
        }
#endif
#if LAMARR_ANTHEIL
        string? sIsaName = Opt(rgArgs, "--isa");
        string? sSeed = Opt(rgArgs, "--seed") ?? Cfg("LamarrSeed");
        //自动种子只用一种拼法 换打包器/TFM也不改字节
        ulong uSeed;
        try { uSeed = sSeed != null ? ParseSeedHex(sSeed) : unchecked(((ulong)s_rng.Next() << 32 | (uint)s_rng.Next()) & 0x7FFFFFFFFFFFFFFFul); }
        catch (Exception eSeed) { Console.Error.WriteLine($"[seed] not hex: {sSeed} ({eSeed.GetType().Name})"); return iErrUsage; }
        if (sSeed == null) Console.WriteLine($"[seed] auto={uSeed:X16} (pass --seed to reproduce this build)");//自选释放种子要留痕 否则无法复现
        string? sRole = Opt(rgArgs, "--role");
        int iRole = sRole != null && sRole.Equals("fake", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        string? sOrder = Opt(rgArgs, "--boot-order-check");
        if (sOrder != null)
        {
            byte[] rgOrder = File.ReadAllBytes(sOrder);//独立入口 对任意boot镜像跑顺序校验
            Console.WriteLine($"[boot-order] image={sOrder} bytes={rgOrder.Length}");
            try { PeWriterAntheil.CheckEncryptOrder(rgOrder); }
            catch (Exception eOrder) { Console.WriteLine($"[boot-order] FAIL {eOrder.Message}"); return iErrCheck; }
            return iOk;
        }
        string? sExpCheck = Opt(rgArgs, "--boot-exp-check");//离线核查期望值块与产物boot是否自洽
        if (sExpCheck != null) return PeWriterAntheil.CheckBootExp(sExpCheck);
        if (Array.Exists(rgArgs, a => a.Equals("--pick-isa", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine(IsaPool.PoolPick(uSeed, iRole));//打印所选规范名
            return iOk;
        }
        if (Array.Exists(rgArgs, a => a.Equals("--carrier", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine(IsaPool.CarrierIsa(uSeed, iRole));//载体ISA池选 没就绪就回退
            return iOk;
        }
        bool bOpTbl = HasFlag(rgArgs, "--op-table-dump");//打印置换表与逆表并自检保形 可选带一个16进制种子
        string sOpTbl = Opt(rgArgs, "--op-table-dump") ?? "";
        if (bOpTbl)
        {
            ulong uS = uSeed;
            try { if (sOpTbl.Length > 0) uS = ParseSeedHex(sOpTbl); }
            catch { Console.WriteLine($"[op-table] bad hex seed: {sOpTbl}"); return iErrCheck; }
            Console.Write(OpTable.Dump(uS));
            bool bOk = OpTable.SelfCheck(uS, out string sBad);
            Console.WriteLine(bOk ? "[op-table] selfcheck ok (shape-preserving, unused bytes identity)" : $"[op-table] SELFCHECK FAIL {sBad}");
            return bOk ? iOk : iErrCheck;
        }
        bool bKernOps = HasFlag(rgArgs, "--kern-ops");//统计内核字节码里能力格出现几次
        if (bKernOps)
        {
            string sStats = NotenrolleComp.KernOpStats();
            Console.Write(sStats);
            return sStats.Contains("RESULT: PASS") ? iOk : iErrCheck;
        }
        string? sEngSum = Opt(rgArgs, "--engine-sum");//打印引擎镜像text摘要 同ISA不同种子摘要不同
        if (sEngSum != null)
        {
            Console.WriteLine(PeWriterAntheil.EngineTextSum(File.ReadAllBytes(sEngSum)).ToString("X8"));
            return iOk;
        }
        string? sNames = Opt(rgArgs, "--dump-names");//打印入口层描述性名字(>2字符) 核对改名覆盖
        if (sNames != null)
        {
            PeWriterAntheil.DumpNames(sNames);
            return iOk;
        }
        bool bOpSig = HasFlag(rgArgs, "--op-table-sigma");//打印操作码置换全表每行16个十六进制字节
        string sOpSig = Opt(rgArgs, "--op-table-sigma") ?? "";
        if (bOpSig)
        {
            ulong uS = uSeed;
            try { if (sOpSig.Length > 0) uS = ParseSeedHex(sOpSig); }
            catch { Console.WriteLine($"[op-table] bad hex seed: {sOpSig}"); return iErrCheck; }
            byte[] rgS = OpTable.BuildSigma(uS);
            Console.WriteLine($"sigma seed={uS:X16} derived=true triage={OpTable.Triage} groups='{OpTable.GroupMask}'");
            for (int i = 0; i < 256; i += 16)
            {
                var rg = new string[16];
                for (int k = 0; k < 16; k++) rg[k] = rgS[i + k].ToString("X2");
                Console.WriteLine(string.Join(" ", rg));
            }
            return iOk;
        }
        bool bOpRt = HasFlag(rgArgs, "--op-table-rt");//编码侧往返自检 只比对操作码位置 查置换表与载体选择保形
        string sOpRt = Opt(rgArgs, "--op-table-rt") ?? "";
        if (bOpRt)
        {
            ulong uS = uSeed;
            try { if (sOpRt.Length > 0) uS = ParseSeedHex(sOpRt); }
            catch { Console.WriteLine($"[op-table] bad hex seed: {sOpRt}"); return iErrCheck; }
            string sIsaRt = Opt(rgArgs, "--isa") ?? "ppc";
            Console.WriteLine($"[op-rt] derived=true triage={OpTable.Triage} isa={sIsaRt} seed={uS:X16}");
            int iBad = 0;
            for (int i = 0; i < NotenrolleComp.KernRollCount; i++)
            {
                byte[] rgBc = NotenrolleComp.CompileKern(i, uS);
                OpTable.Begin(uS, sIsaRt);
                CarrierFmt.Begin(uS);//载体宽度码表
                byte[] rgEnc = sIsaRt switch
                {
                    "rv64" => NotenrolleIsaRv.EncodeLinear(rgBc),
                    "mips64" => NotenrolleIsaMi.EncodeLinear(rgBc),
                    "loongarch" => NotenrolleIsaLa.EncodeLinear(rgBc),
                    "alpha" => NotenrolleIsaAl.EncodeLinear(rgBc),
                    _ => NotenrolleIsa.EncodeLinear(rgBc),
                };
                byte[]? rgDec = sIsaRt switch
                {
                    "rv64" => NotenrolleIsaRv.DecodeLinear(rgEnc),
                    "mips64" => NotenrolleIsaMi.DecodeLinear(rgEnc),
                    "loongarch" => NotenrolleIsaLa.DecodeLinear(rgEnc),
                    "alpha" => NotenrolleIsaAl.DecodeLinear(rgEnc),
                    _ => NotenrolleIsa.DecodeLinear(rgEnc),
                };
                if (rgDec == null) { Console.WriteLine($"[op-rt] kern {i} DECODE_NULL"); iBad++; continue; }
                var rgOps = NotenrolleCfg.Disasm(rgBc);//只对操作码位置过表 操作数字节必须与规范流逐字节相同
                var rgIsOp = new bool[rgBc.Length];
                foreach (var t in rgOps) if (t.iPos >= 0 && t.iPos < rgIsOp.Length) rgIsOp[t.iPos] = true;
                var rgExp = (byte[])rgBc.Clone();
                for (int k = 0; k < rgExp.Length; k++) if (rgIsOp[k]) rgExp[k] = OpTable.Map(rgBc[k]);
                int iAt = -1;
                if (rgBc.Length != rgDec.Length) iAt = Math.Min(rgBc.Length, rgDec.Length);
                else for (int k = 0; k < rgBc.Length; k++) if (rgExp[k] != rgDec[k]) { iAt = k; break; }
                if (iAt < 0)
                {
                    Console.WriteLine($"[op-rt] kern {i} OK bc={rgBc.Length} enc={rgEnc.Length} ops={rgOps.Count}");
                    continue;
                }
                iBad++;
                string sA = iAt < rgBc.Length ? rgBc[iAt].ToString("X2") : "EOF";
                string sB = iAt < rgDec.Length ? rgDec[iAt].ToString("X2") : "EOF";
                string sC = iAt < rgExp.Length ? rgExp[iAt].ToString("X2") : "EOF";
                string sTag = iAt < rgIsOp.Length && rgIsOp[iAt] ? " (opcode)" : " (operand)";
                Console.WriteLine($"[op-rt] kern {i} MISMATCH@{iAt}{sTag} bc={sA} want={sC} dec={sB} bclen={rgBc.Length} declen={rgDec.Length}");
                int iLo = Math.Max(0, iAt - 6), iHi = Math.Min(rgBc.Length, iAt + 6);
                Console.WriteLine($@"        bc[..]  = {BitConverter.ToString(rgBc, iLo, iHi - iLo).Replace("-", " ")}");
                Console.WriteLine($@"        want[]  = {BitConverter.ToString(rgExp, iLo, iHi - iLo).Replace("-", " ")}");
                Console.WriteLine($@"        dec[]   = {BitConverter.ToString(rgDec, iLo, Math.Min(iHi, rgDec.Length) - iLo).Replace("-", " ")}");
            }
            Console.WriteLine(iBad == 0 ? "[op-rt] ALL OK" : $"[op-rt] FAIL {iBad}");
            for (int i = 0; i < NotenrolleComp.KernRollCount; i++)//操作码直方图
            {
                byte[] rgBc = NotenrolleComp.CompileKern(i, uS);
                var rgOps2 = NotenrolleCfg.Disasm(rgBc);
                var rgHist = new SortedDictionary<int, int>();
                foreach (var t in rgOps2)
                {
                    int b = rgBc[t.iPos];
                    rgHist[b] = rgHist.TryGetValue(b, out int iC) ? iC + 1 : 1;
                }
                string sHist = string.Join(",", rgHist.Select(kv => $"{kv.Key:X2}x{kv.Value}"));
                Console.WriteLine($"[op-rt] kern {i} ops={sHist}");
            }
            return iBad == 0 ? iOk : iErrCheck;
        }
        string? sEmitEngine = Opt(rgArgs, "--emit-engine");//生成引擎汇编写到文件
        string? sUnpack = Opt(rgArgs, "--unpack");//离线还原产物容器与各region
        if (sUnpack != null)
        {
            string sOutU = Opt(rgArgs, "--out") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sUnpack))!, "unpack");
            return PeWriterAntheil.Unpack(sUnpack, sOutU,
                Array.Exists(rgArgs, a => a.Equals("--scan", StringComparison.OrdinalIgnoreCase)),
                Opt(rgArgs, "--escrow"));
        }
        string sCarrierIsa = sIsaName ?? IsaPool.CarrierIsa(uSeed, iRole);//载体ISA需引擎解码与打包端编码都就绪 否则回退ppc
        if (sEmitEngine != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(sEmitEngine))!);
            File.WriteAllText(sEmitEngine, PianolaEngine.Generate(uSeed, sCarrierIsa));
            Console.WriteLine($"[emit-engine] -> {sEmitEngine} (seed={uSeed:X16} carrierIsa={sCarrierIsa})");
            return iOk;
        }
        string? sSlotMorph = Opt(rgArgs, "--slot-morph");//等长插槽重写 打包器与构建链共用同一实现
        if (sSlotMorph != null)
        {
            return PeWriterAntheil.SlotMorph(sSlotMorph, Opt(rgArgs, "--out"), uSeed,
                Opt(rgArgs, "--tag") ?? Path.GetFileNameWithoutExtension(sSlotMorph),
                Array.Exists(rgArgs, a => a.Equals("--check", StringComparison.OrdinalIgnoreCase)));
        }
        string? sEscrow = Opt(rgArgs, "--escrow") ?? Cfg("LamarrEscrow");//escrow清单 需显式开启
        string? sReplay = Opt(rgArgs, "--replay") ?? Cfg("LamarrReplay");//用清单里的vmSeed与head重打包

        string? sEncryptDeps = Opt(rgArgs, "--encrypt-deps") ?? Cfg("EncryptDeps");
        string? sMethodDict = Opt(rgArgs, "--method-dict") ?? Cfg("LamarrMethodDict");
        string? sNoCompressDeps = Opt(rgArgs, "--no-compress-deps") ?? Cfg("NoCompressDeps");
#endif
        string? sRtcVersion = Opt(rgArgs, "--rtc-version") ?? Cfg("RtcVersion");
        string? sTiered = Opt(rgArgs, "--rtc-tiered") ?? Cfg("RtcTiered");
        string? sPayloadMajor = Opt(rgArgs, "--payload-major") ?? Cfg("PayloadMajor");//载荷TFM主版本 它就是stub的PREFMAJ
        int iPayloadMajor = 0;
        if (sPayloadMajor != null) int.TryParse(sPayloadMajor, out iPayloadMajor);
        string? sRollForward = Opt(rgArgs, "--rtc-rollforward") ?? Cfg("RtcRollForward");//null=沿用载荷值 未发布则用默认策略 取该主版本最新运行时 缺失取更高一级
        string? sTargetArg = Opt(rgArgs, "-Target") ?? Cfg("LamarrTarget");
        if (sTargetArg == null && sInput == null) sTargetArg = DiscoverTarget();//零参数发现 cwd下唯一csproj
        string? sProjDir, sProjName, sProjTfm;
        if (sTargetArg != null)
        {
            string? sResolved = ResolveTargetInput(sTargetArg, sMode, out sProjDir, out sProjName, out sProjTfm);
            if (sInput == null) sInput = sResolved;
        }
        else sProjDir = sProjName = sProjTfm = null;
        bool bAntheil = sMode.Equals("antheil", StringComparison.OrdinalIgnoreCase);
        if (sStub == null) sStub = Convention(Path.Combine("Lamarr", "Native", "out", bAntheil ? "antheil_stub.dll" : "lamarr_stub.dll"));
        if (sBoot == null && !sMode.Equals("managed", StringComparison.OrdinalIgnoreCase))
        {
            if (bAntheil && sProjName != null && sProjTfm != null)
                sBoot = Convention(Path.Combine("Lamarr", "Native", "BootAntheil", "bin", "Release", sProjTfm, $"{sProjName}.Boot.dll"));
            else if (!bAntheil && sProjTfm != null)
                sBoot = Convention(Path.Combine("Lamarr", "Native", "Boot", "bin", "Release", sProjTfm, "LamarrBoot.dll"));
        }
#if LAMARR_ANTHEIL
        if (bAntheil)
        {
            if (sDecoder == null) sDecoder = Convention(Path.Combine("Lamarr", "Native", "out", "Iamdec.dll"));
            if (sJitHook == null) sJitHook = Convention(Path.Combine("Lamarr", "Native", "out", "jithook.dll"));
            if (sEngine == null) sEngine = Convention(Path.Combine("Lamarr", "Native", "out", "engine.dll"));
        }
#endif
        if (sOutput == null && sProjDir != null && sProjTfm != null && sProjName != null)
        {
            sOutput = Path.Combine(sProjDir, "bin", "Release", sProjTfm, "win-x64", "publish", $"{sProjName}_packed.exe");//载荷工程发布目录下的产物名
            Console.WriteLine($"[discover] output={sOutput}");
        }
        if (HasFlag(rgArgs, "-Configure") || HasFlag(rgArgs, "-Fresh"))
            return Configure(rgArgs);
        if (sStub == null || sInput == null || sOutput == null)
        {
            ShowHelp();
            return iErrUsage;
        }
        if (!File.Exists(sStub)) { Console.Error.WriteLine($"Stub not found: {sStub}"); return iErrUsage; }
        if (!File.Exists(sInput)) { Console.Error.WriteLine($"Input not found: {sInput}"); return iErrUsage; }
        if (sBoot != null && !File.Exists(sBoot)) { Console.Error.WriteLine($"Boot not found: {sBoot}"); return iErrUsage; }
#if LAMARR_ANTHEIL
        if (sShim != null && !File.Exists(sShim)) { Console.Error.WriteLine($"Entry layer not found: {sShim}"); return iErrUsage; }
        if (sDecoder != null && !File.Exists(sDecoder)) { Console.Error.WriteLine($"Decoder not found: {sDecoder}"); return iErrUsage; }
        if (sJitHook != null && !File.Exists(sJitHook)) { Console.Error.WriteLine($"JitHook not found: {sJitHook}"); return iErrUsage; }
#endif

        if (sPayloadMajor == null || sRtcVersion == null || sTiered == null)
        {
            string sIn = sInput!;
            if (PeWriter.PeekPayload(sIn, out int iMj, out string? sVer, out string? sTr))//未显式传参时从载荷自身派生
            {
                if (sPayloadMajor == null && iMj > 0) iPayloadMajor = iMj;
                if (sRtcVersion == null) sRtcVersion = sVer;
                if (sTiered == null) sTiered = sTr;
                Console.WriteLine($"[payload] derived major={iMj} version={sVer ?? "-"} tiered={sTr ?? "-"}");
            }
        }
        try
        {
            Console.WriteLine($"Patching {Path.GetFileName(sInput)} ({sMode} mode)...");
            if (sMode.Equals("managed", StringComparison.OrdinalIgnoreCase))
            {
                BundleWriter.Pack(sStub, sInput, sOutput);
            }
            else if (sMode.Equals("clean", StringComparison.OrdinalIgnoreCase) || sMode.Equals("native", StringComparison.OrdinalIgnoreCase))
            {
                var pe = new PeWriter();
                pe.LoadStub(sStub);
                if (sBoot != null) pe.LoadBoot(sBoot);
                if (sRtcVersion != null) pe.SetRtcVersion(sRtcVersion);
                if (sTiered != null) pe.SetTiered(sTiered);
                pe.SetPayloadMajor(iPayloadMajor);
                pe.SetRollForward(sRollForward);
                pe.LoadPayload(sInput);
                pe.Pack(sOutput);
            }
#if LAMARR_ANTHEIL
            else
            {
                Console.WriteLine($"  seed: {uSeed:X16}");//坏产物必须能从种子重建
                var pe = new PeWriterAntheil();
                pe.LoadStub(sStub);
                if (!string.IsNullOrEmpty(sMethodDict)) pe.SetMethodDict(sMethodDict);//改名要用它 必须在LoadBoot之前
                pe.SetVmSeed(uSeed);//种子要在LoadBoot之前 LoadBoot里就生成头部并按派生形状
                if (!string.IsNullOrEmpty(sReplay))
                {
                    var oEsc = JsonDocument.Parse(PeWriterAntheil.ReadEscrowJson(sReplay)).RootElement;//用上一次发布的清单重打包
                    pe.SetVmSeed(ParseSeedHex(oEsc.GetProperty("vmSeed").GetString()!));
                    pe.SetReplayHead(Convert.FromHexString(oEsc.GetProperty("head").GetString()!));
                    Console.WriteLine($"[replay] material from {sReplay} (vmSeed and head)");
                }
                if (sBoot != null) pe.LoadBoot(sBoot);
                if (sShim != null) pe.LoadShim(sShim);
                if (sDecoder != null) pe.LoadDecoder(sDecoder);
                if (sJitHook != null) pe.LoadJitHook(sJitHook);
                pe.LoadPheropod(sPheropod ?? "");
                if (sEngine != null) pe.LoadEngine(sEngine);
                if (sEscrow != null) pe.SetEscrow(sEscrow, Array.Exists(rgArgs, a => a.Equals("--escrowPlain", StringComparison.OrdinalIgnoreCase)));
                if (sRtcVersion != null) pe.SetRtcVersion(sRtcVersion);
                if (sTiered != null) pe.SetTiered(sTiered);
                pe.SetPayloadMajor(iPayloadMajor);
                pe.SetRollForward(sRollForward);
                  if (Array.Exists(rgArgs, a => a.Equals("--compress-deps", StringComparison.OrdinalIgnoreCase)))
                      pe.SetCompressDeps(true);
                  if (!string.IsNullOrEmpty(sEncryptDeps))
                      pe.SetEncryptDeps(sEncryptDeps);
                  if (!string.IsNullOrEmpty(sNoCompressDeps))
                      pe.SetNoCompressDeps(sNoCompressDeps);
                pe.LoadPayload(sInput);
                pe.Pack(sOutput);
            }
#else
            else
            {
                Console.Error.WriteLine($"Mode not in this build: {sMode} (available: {Modes()})");
                return iErrUnavailable;
            }
#endif
            return iOk;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex}");
            return iErrException;
        }
    }

    static string Modes()
    {
#if LAMARR_ANTHEIL
        return "managed|native|antheil";
#else
        return "managed|native";
#endif
    }

    static void ShowHelp()
    {
        string sExe = ExeName();
        Console.Error.WriteLine();
        Console.Error.WriteLine($@"LamarrPacker {Version()} - Lamarr packer
  tfm={TfmName()} antheil={AntheilFlag()} modes={Modes()}

Usage:
  {sExe}.exe <mode> --stub <stub> --input <input.exe> --output <output.exe> [options]
  {sExe}.exe -Target <payload.csproj|dir|name> --stub <stub> --output <output.exe> [options]
  {sExe}.exe [<mode>]                zero-arg discovery, sole csproj in cwd plus conventional stub/boot/output
  {sExe}.exe --version | --help

Modes: {Modes()}  (native is an alias of clean)

Pack options:
  --stub <path>        stub image (single-file bundle or asm stub)
  --input <path>       payload, the published exe in every mode
  --output <path>      output exe
  --mode <mode>        default clean, a bare first argument is also accepted
  --boot <dll>         bootstrapper (native antheil)
  --rtc-version <v> --rtc-tiered <v> --rtc-rollforward <v> --payload-major <n>");
#if LAMARR_ANTHEIL
        Console.Error.WriteLine(@"  antheil inputs: --shim <host.dll> --decoder <Iamdec.dll> --jithook <jithook.dll> --pheropod <z0.dll, the packed bait is mutated in place per release seed> --engine <engine.dll>
                --seed <hex> --method-dict <file> --encrypt-deps <list> --no-compress-deps <list> --escrow <json> --replay <json>");
#endif
        Console.Error.WriteLine($@"
Global options:
  -D <dir>             work dir, basis for relative paths and discovery
  -Target <path|name>  payload project, locates the input when --input is absent (managed: dll under bin, others: exe under publish)
  -Configure           merge explicit args into lamarrpack.props and exit
  -Fresh               rewrite lamarrpack.props from scratch
  --out <dir|file>     config location, default next to the tool exe
  --config <file>      config file to read, default cwd first then next to the exe
  --version            print version, tfm, antheil and available modes
  --help, -h, /?       print this help
  auto discovery       fills -Target/--stub/--boot/--output from the cwd csproj and conventional paths

Return codes:
  0 ok   1 usage   2 self-check failed   3 mode not in this build   4 unknown command   5 exception");
#if LAMARR_ANTHEIL
        Console.Error.WriteLine(@"
Tools:
  --boot-order-check <img> --boot-exp-check <exe> --pick-isa --carrier
  --op-table-dump [seed] --op-table-sigma [seed] --op-table-rt [seed] [--isa <name>] --kern-ops
  --engine-sum <dll> --dump-names <exe> --emit-engine <asm> --unpack <exe> [--out <dir>] [--scan]");
#endif
        Console.Error.WriteLine();
    }

    static void ShowVersion()
    {
        Console.WriteLine($@"LamarrPacker {Version()}
  tfm     : {TfmName()}
  antheil : {AntheilFlag()}
  modes   : {Modes()}");
    }

    static string ExeName()
    {
#if NET6_0_OR_GREATER
        string? sPath = Environment.ProcessPath;
#else
        string? sPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;//net5没有Environment.ProcessPath
#endif
        return Path.GetFileNameWithoutExtension(sPath ?? "LamarrPacker");
    }

    static string Version() => typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    static string TfmName() => AppContext.TargetFrameworkName ?? "?";

    static string AntheilFlag()
    {
        foreach (object oAttr in typeof(Program).Assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
            if (oAttr is AssemblyMetadataAttribute md && md.Key == "LamarrAntheil")
                return md.Value ?? "?";
        return "?";
    }

    static bool HasFlag(string[] rgArgs, string sName) =>
        Array.Exists(rgArgs, a => a.Equals(sName, StringComparison.OrdinalIgnoreCase));

    static bool IsMode(string sMode)
    {
        string s = sMode.ToLowerInvariant();
        if (s == "managed" || s == "native" || s == "clean") return true;
#if LAMARR_ANTHEIL
        if (s == "antheil") return true;
#endif
        return false;
    }

    static int Configure(string[] rgArgs)
    {
        string sPath = OutPath(rgArgs);
        bool bFresh = HasFlag(rgArgs, "-Fresh");
        var mp = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!bFresh) ReadProps(sPath, mp);
        Put(mp, "Packer", Opt(rgArgs, "--mode") ?? CmdArg(rgArgs));
        Put(mp, "LamarrTarget", Opt(rgArgs, "-Target"));
        Put(mp, "LamarrStub", Opt(rgArgs, "--stub"));
        Put(mp, "LamarrInput", Opt(rgArgs, "--input"));
        Put(mp, "LamarrOutput", Opt(rgArgs, "--output"));
        Put(mp, "LamarrBoot", Opt(rgArgs, "--boot"));
        Put(mp, "RtcVersion", Opt(rgArgs, "--rtc-version"));
        Put(mp, "RtcTiered", Opt(rgArgs, "--rtc-tiered"));
        Put(mp, "RtcRollForward", Opt(rgArgs, "--rtc-rollforward"));
        Put(mp, "PayloadMajor", Opt(rgArgs, "--payload-major"));
        Put(mp, "LamarrSeed", Opt(rgArgs, "--seed"));//只落显式给的种子 免得随机值进配置
#if LAMARR_ANTHEIL
        Put(mp, "ShimDll", Opt(rgArgs, "--shim"));
        Put(mp, "LamarrDecoder", Opt(rgArgs, "--decoder"));
        Put(mp, "LamarrJitHook", Opt(rgArgs, "--jithook"));
        Put(mp, "LamarrPheropod", Opt(rgArgs, "--pheropod"));
        Put(mp, "EngineDll", Opt(rgArgs, "--engine"));
        Put(mp, "EncryptDeps", Opt(rgArgs, "--encrypt-deps"));
        Put(mp, "NoCompressDeps", Opt(rgArgs, "--no-compress-deps"));
        Put(mp, "LamarrMethodDict", Opt(rgArgs, "--method-dict"));
        Put(mp, "LamarrEscrow", Opt(rgArgs, "--escrow"));
        Put(mp, "LamarrReplay", Opt(rgArgs, "--replay"));
#endif
        if (mp.Count == 0 && !File.Exists(sPath))
        {
            Console.WriteLine("[configure] nothing to write (no explicit args)");
            return iOk;
        }
        WriteProps(sPath, mp);
        Console.WriteLine($"[configure] fresh={bFresh} keys={mp.Count} -> {sPath}");
        return iOk;
    }

    static string? CmdArg(string[] rgArgs) => !rgArgs[0].StartsWith("-") ? rgArgs[0] : null;

    static void Put(IDictionary<string, string> mp, string sName, string? sVal)
    {
        if (!string.IsNullOrEmpty(sVal)) mp[sName] = sVal!;
    }

    static string OutPath(string[] rgArgs)//缺省写在工具exe同目录 随单文件分发携带
    {
        string? sOut = Opt(rgArgs, "--out");
        if (string.IsNullOrEmpty(sOut)) return Path.Combine(AppContext.BaseDirectory, "lamarrpack.props");
        if (Directory.Exists(sOut) || Path.GetExtension(sOut).Length == 0) return Path.Combine(sOut, "lamarrpack.props");
        return sOut;
    }

    static void LoadConfig(string[] rgArgs)//命令行优先于配置 配置优先于默认
    {
        string sFile = "lamarrpack.props";
        string? sExplicit = Opt(rgArgs, "--config");
        if (sExplicit != null) s_sCfgPath = sExplicit;
        else if (File.Exists(Path.Combine(Environment.CurrentDirectory, sFile))) s_sCfgPath = Path.Combine(Environment.CurrentDirectory, sFile);
        else if (File.Exists(Path.Combine(AppContext.BaseDirectory, sFile))) s_sCfgPath = Path.Combine(AppContext.BaseDirectory, sFile);
        if (s_sCfgPath != null && File.Exists(s_sCfgPath)) ReadProps(s_sCfgPath, s_mpCfg);
    }

    static string? Cfg(string sName) => s_mpCfg.TryGetValue(sName, out string? sVal) ? sVal : null;

    static void ReadProps(string sPath, IDictionary<string, string> mp)
    {
        if (!File.Exists(sPath)) return;
        foreach (Match m in Regex.Matches(File.ReadAllText(sPath), "<([A-Za-z_][A-Za-z0-9_]*)>([^<]*)</\\1>"))
        {
            string sName = m.Groups[1].Value;
            if (sName == "Project" || sName == "PropertyGroup") continue;
            mp[sName] = Unesc(m.Groups[2].Value.Trim());
        }
    }

    static void WriteProps(string sPath, IEnumerable<KeyValuePair<string, string>> rg)
    {
        string s = "<Project>\n  <PropertyGroup>\n";
        foreach (var kv in rg) s += $"    <{kv.Key}>{Esc(kv.Value)}</{kv.Key}>\n";
        s += "  </PropertyGroup>\n</Project>\n";
        File.WriteAllText(sPath, s);
    }

    static string Esc(string sVal) => sVal.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    static string Unesc(string sVal) => sVal.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");

    static string? ResolveTargetInput(string? sTarget, string sMode, out string? sProjDir, out string? sProjName, out string? sProjTfm)
    {
        sProjDir = sProjName = sProjTfm = null;
        if (string.IsNullOrEmpty(sTarget)) return null;
        string sProj = sTarget;
        if (Directory.Exists(sProj))
        {
            string[] rgProj = Directory.GetFiles(sProj, "*.csproj");
            if (rgProj.Length == 0) { Console.Error.WriteLine($"Target project not found in dir: {sProj}"); return null; }
            sProj = rgProj[0];
        }
        else if (!File.Exists(sProj))
        {
            string sGuess = Path.Combine(sProj, $"{Path.GetFileName(sProj.TrimEnd('\\', '/'))}.csproj");
            if (!File.Exists(sGuess)) { Console.Error.WriteLine($"Target not found: {sTarget}"); return null; }
            sProj = sGuess;
        }
        string sText = File.ReadAllText(sProj);
        string sName = XmlValue(sText, "AssemblyName") ?? Path.GetFileNameWithoutExtension(sProj);
        string? sTfm = XmlValue(sText, "TargetFramework") ?? XmlValue(sText, "TargetFrameworks");
        if (string.IsNullOrEmpty(sTfm)) { Console.Error.WriteLine($"Target TFM not found: {sProj}"); return null; }
        sProjDir = Path.GetDirectoryName(Path.GetFullPath(sProj))!;
        sProjName = sName;
        sProjTfm = sTfm.Split(';')[0].Trim();
        bool bManaged = sMode.Equals("managed", StringComparison.OrdinalIgnoreCase);
        string sDir = Path.Combine(sProjDir, "bin", "Release", sProjTfm, "win-x64");
        string sIn = bManaged ? Path.Combine(sDir, $"{sName}.dll") : Path.Combine(sDir, "publish", $"{sName}.exe");
        if (!File.Exists(sIn)) { Console.Error.WriteLine($"Payload not found: {sIn}"); return null; }
        Console.WriteLine($"[discover] target={sProj}");
        Console.WriteLine($"[discover] input={sIn}");
        return sIn;
    }

    static string? DiscoverTarget()//cwd唯一csproj才当载荷工程 多份需显式-Target
    {
        string[] rgProj = Directory.GetFiles(Environment.CurrentDirectory, "*.csproj");
        if (rgProj.Length == 1) return rgProj[0];
        if (rgProj.Length > 1) Console.Error.WriteLine($"Multiple csproj in work dir, pass -Target: {string.Join(", ", rgProj)}");
        return null;
    }

    static string? RepoRoot()//向上找仓库标志定位stub/boot约定 最多6级
    {
        DirectoryInfo? dir = new DirectoryInfo(Environment.CurrentDirectory);
        for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Lamarr", "Native", "PayloadPack.targets"))) return dir.FullName;
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
        }
        return null;
    }

    static string? Convention(string sRel)//仓库约定路径 缺失返回null
    {
        string? sRoot = RepoRoot();
        if (sRoot == null) return null;
        string sPath = Path.Combine(sRoot, sRel);
        if (!File.Exists(sPath)) return null;
        Console.WriteLine($"[discover] {Path.GetFileName(sPath)}={sPath}");
        return sPath;
    }

    static string? XmlValue(string sText, string sProp)
    {
        Match m = Regex.Match(sText, $"<{sProp}>([^<]+)</{sProp}>");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    static ulong ParseSeedHex(string sVal)//0x前缀与裸十六进制都收 与 --op-table-rt 一类开关用同一条规矩
        => Convert.ToUInt64(sVal.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? sVal.Substring(2) : sVal, 16);

    static string? Opt(string[] rgArgs, string sName)
    {
        int i = Array.FindIndex(rgArgs, a => a.Equals(sName, StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= rgArgs.Length) return null;
        string sVal = rgArgs[i + 1];
        return sVal.StartsWith("--") ? null : sVal;//值不能是另一个长开关
    }
}
