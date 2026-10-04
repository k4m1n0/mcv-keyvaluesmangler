using System.Reflection;
using System.Text;
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

    internal static int Main(string[] rgArgs)
    {
        Triage.Banner(rgArgs);//只在确实设了triage旋钮且命令会产生产物时打一行
        if (rgArgs.Length == 0)
        {
            ShowHelp();
            return iErrUsage;
        }
        int iDash = Array.IndexOf(rgArgs, "--");//--之后的token一律视为值
        if (HasFlag(rgArgs, "--help"))
        {
            ShowHelp();
            return iOk;
        }
        if (HasFlag(rgArgs, "--version"))
        {
            ShowVersion();
            return iOk;
        }
        //退役守卫 故意保留以响亮报错 不静默忽略
        if (Opt(rgArgs, "--seed") != null || HasFlag(rgArgs, "--seed"))
        {
            Console.Error.WriteLine("[key] --seed is not supported: pass --key <64hex> or --key-file <path>");
            return iErrUsage;
        }
        if (HasFlag(rgArgs, "--escrowPlain"))
        {
            Console.Error.WriteLine("[escrow] --escrowPlain is not supported: a plaintext escrow would hand out the whole recipe; use --diag for non-sensitive diagnostics");
            return iErrUsage;
        }
        //命令是首个命令记号 表外记号一律硬报错 三种构建都做识别与校验
        string? sCmd = null;
        int iCmd = -1, iEndScan = iDash < 0 ? rgArgs.Length : iDash;
        for (int i = 0; i < iEndScan; i++)
        {
            if (!IsCommand(rgArgs[i])) continue;
            sCmd = rgArgs[i]; iCmd = i; break;
        }
        if (sCmd == null)
        {
            if (HasFlag(rgArgs, "--configure") || HasFlag(rgArgs, "--fresh")) return Configure(rgArgs);
            Console.Error.WriteLine("[cli] no command given (expected one of: " + string.Join(" ", rgCommands) + ")");
            ShowHelp();
            return iErrUsage;
        }
        int iVerdict = ValidateTokens(rgArgs, sCmd, iDash);
        if (iVerdict != iOk) return iVerdict;
        string? sPos1 = Pos(rgArgs, iCmd);
        string? sPos2 = sPos1 != null ? Pos(rgArgs, iCmd + 1) : null;
        string sVerb = sCmd.Equals("--internal", StringComparison.OrdinalIgnoreCase) ? (sPos1 ?? "").ToLowerInvariant() : "";
        string sSub = sCmd.Equals("--selftest", StringComparison.OrdinalIgnoreCase) ? (sPos1 ?? "").ToLowerInvariant() : "";
        if (sVerb.Length > 0 && !Array.Exists(rgInternalVerbs, a => a.Equals(sVerb, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine($"[internal] unknown verb: {sVerb} (" + string.Join("|", rgInternalVerbs) + ")");
            return iErrUsage;
        }
#if !LAMARR_ANTHEIL
        //关闭antheil的构建只有--pack 其余命令在编译期不存在 一律响亮拒绝
        if (!sCmd.Equals("--pack", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"[cli] {sCmd} is not in this build (antheil-only): rebuild with -p:LamarrAntheil=1");
            return iErrUnavailable;
        }
#endif
#if LAMARR_ANTHEIL
        //命令级动词 每个命令只认自己那几项ValidateTokens已先校验
        if (sCmd.Equals("--escrow-keygen", StringComparison.OrdinalIgnoreCase))
        {
            if (sPos1 == null) { Console.Error.WriteLine("[escrow-keygen] needs <private-key-file> [--pub <public-key-file>]"); return iErrUsage; }
            EscrowKey.Keygen(sPos1, Opt(rgArgs, "--pub") ?? (sPos1 + ".pub"));//私钥留本机离线 公钥可上构建机
            return iOk;
        }
        if (sCmd.Equals("--escrow-key-info", StringComparison.OrdinalIgnoreCase))
        {
            if (sPos1 == null) { Console.Error.WriteLine("[escrow-key-info] needs <key-file>"); return iErrUsage; }
            EscrowKey.Info(sPos1);
            return iOk;
        }
        if (sCmd.Equals("--keygen", StringComparison.OrdinalIgnoreCase))
        {
            if (sPos1 == null) { Console.Error.WriteLine("[keygen] needs <key-file>"); return iErrUsage; }
            KeyFile.Keygen(sPos1);//带LAMARR-KEY标签的钥匙文件
            return iOk;
        }
        if (sCmd.Equals("--key-info", StringComparison.OrdinalIgnoreCase))
        {
            if (sPos1 == null) { Console.Error.WriteLine("[key-info] needs <key-file>"); return iErrUsage; }
            KeyFile.Info(sPos1);
            return iOk;
        }
        if (sCmd.Equals("--verify", StringComparison.OrdinalIgnoreCase))
        {
            string? sArt = Opt(rgArgs, "--artifact");
            if (sArt == null) { Console.Error.WriteLine("[verify] needs --artifact <exe> [--expect <64hex|@file>]"); return iErrUsage; }
            if (!File.Exists(sArt)) { Console.Error.WriteLine($"[verify] not found: {sArt}"); return iErrUsage; }
            string? sExpect = Opt(rgArgs, "--expect");
            if (sExpect != null && sExpect.StartsWith("@"))//@path从文件读
            {
                string sExpPath = sExpect.Substring(1);
                if (!File.Exists(sExpPath)) { Console.Error.WriteLine($"[verify] --expect @{sExpPath}: file not found"); return iErrUsage; }
                sExpect = File.ReadAllText(sExpPath).Trim();
            }
            //不给--expect时只证明自签后未被改过 不等于官方来源
            return PeWriterAntheil.VerifySealFile(sArt, sExpect);
        }
        if (sCmd.Equals("--selftest", StringComparison.OrdinalIgnoreCase))
        {
            if (sSub.Length == 0) { Console.Error.WriteLine("[selftest] needs <name>: escrow|seal|leak"); return iErrUsage; }
            if (sSub == "escrow")
            {
                if (sPos2 == null) { Console.Error.WriteLine("[selftest escrow] needs <dir>"); return iErrUsage; }
                return PeWriterAntheil.EscrowSelfTest(sPos2);//信封不变量自检 秒级 不需真打包
            }
            if (sSub == "seal")
            {
                if (sPos2 == null) { Console.Error.WriteLine("[selftest seal] needs <dir>"); return iErrUsage; }
                return PeWriterAntheil.SealSelfTest(sPos2);//签名锚自检
            }
            if (sSub == "leak")
            {
                if (sPos2 == null) { Console.Error.WriteLine("[selftest leak] needs <exe> --key <64hex>"); return iErrUsage; }
                if (!File.Exists(sPos2)) { Console.Error.WriteLine($"[leak] not found: {sPos2}"); return iErrUsage; }
                string sLeakKeyHex = Opt(rgArgs, "--key") ?? Cfg("LamarrKey") ?? "";
                if (sLeakKeyHex.Length != 64) { Console.Error.WriteLine("[leak] needs --key <64hex> (the build key that produced the artifact)"); return iErrUsage; }
                byte[] rgLeakK = Convert.FromHexString(sLeakKeyHex);
                var rgHit = PeWriterAntheil.MaterialLeak(File.ReadAllBytes(sPos2), rgLeakK, PeWriterAntheil.KdfSeedHex(rgLeakK));
                Console.WriteLine(rgHit.Count == 0 ? $"[leak] RESULT PASS ({sPos2})" : $"[leak] RESULT FAIL ({string.Join(", ", rgHit)})");
                return rgHit.Count == 0 ? iOk : iErrCheck;
            }
            Console.Error.WriteLine($"[selftest] unknown name: {sPos1} (escrow|seal|leak)");
            return iErrUsage;
        }
        if (sCmd.Equals("--internal", StringComparison.OrdinalIgnoreCase))
        {
            if (sVerb.Length == 0)
            {
                Console.Error.WriteLine("[internal] needs a verb: shape|pick-isa|carrier|emit-engine|slot-morph|boot-order-check");
                return iErrUsage;
            }
            if (sVerb == "shape")
            {
                //取种子口 构建侧用--of-key 重放侧用--of-escrow 都不需要另一侧的东西
                string? sOfKey = Opt(rgArgs, "--of-key");
                string? sOfEsc = Opt(rgArgs, "--of-escrow");
                if (sOfKey != null && sOfEsc != null) { Console.Error.WriteLine("[shape] --of-key and --of-escrow are mutually exclusive"); return iErrUsage; }
                if (sOfKey != null)
                {
                    try
                    {
                        string sRaw = sOfKey.Replace("-", "").Replace(" ", "").Trim();
                        if (sRaw.Length != 64) throw new InvalidOperationException("want 64 hex chars");
                        Console.WriteLine(PeWriterAntheil.Kdf64(Convert.FromHexString(sRaw), "chain:vmseed").ToString("X16"));//脚本只取第一行
                    }
                    catch (Exception eShape) { Console.Error.WriteLine("[shape] " + eShape.Message); return iErrUsage; }
                    return iOk;
                }
                if (sOfEsc != null)
                {
                    string sSecSeed = PeWriterAntheil.ReadEscrowJson(sOfEsc, Opt(rgArgs, "--escrow-priv") ?? Cfg("LamarrEscrowKey"));
                    using var oSeed = JsonDocument.Parse(sSecSeed);
                    if (!oSeed.RootElement.TryGetProperty("vmSeed", out var eVmSeed) || string.IsNullOrEmpty(eVmSeed.GetString()))
                    { Console.Error.WriteLine("[shape] the escrow carries no 'vmSeed': cannot derive the shape seed"); return iErrUsage; }
                    Console.WriteLine(Convert.ToUInt64(eVmSeed.GetString()!, 16).ToString("X16"));//脚本只取第一行
                    return iOk;
                }
                Console.Error.WriteLine("[shape] needs --of-key <64hex> or --of-escrow <escrow> [--escrow-priv <key>]");
                return iErrUsage;
            }
        }
#endif
        string? sWorkDir = Opt(rgArgs, "--workdir");//相对路径与自动发现的基准
        if (sWorkDir != null)
        {
            if (!Directory.Exists(sWorkDir)) { Console.Error.WriteLine($"Work dir not found: {sWorkDir}"); return iErrUsage; }
            Directory.SetCurrentDirectory(sWorkDir);
        }
        LoadConfig(rgArgs);
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
        //缺省是managed antheil是编译期可选必须显式请求managed与native与clean永远在
        string sMode = Opt(rgArgs, "--mode") ?? Cfg("Packer") ?? "managed";
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
        //没有构建种子 离线动词从K派生 这里的随机值只在没给K时供这些动词使用 产物密钥只认--key与--key-file
        ulong uSeed = unchecked(((ulong)s_rng.Next() << 32 | (uint)s_rng.Next()) & 0x7FFFFFFFFFFFFFFFul);
        //打包根密钥K由--key或--key-file给出 缺省随机并响亮告警K决定产物密钥 与离线动词的形状种子分开
        string? sKeyHex = Opt(rgArgs, "--key") ?? Cfg("LamarrKey");
        string? sKeyFileArg = Opt(rgArgs, "--key-file") ?? Cfg("LamarrKeyFile");
        bool bKeyGiven = !string.IsNullOrEmpty(sKeyHex) || !string.IsNullOrEmpty(sKeyFileArg);
        //形状种子S是escrow的vmSeed 16hex 只喂离线动词与重放机K是身份S是形状 两者不许同时出现
        string? sSeedHex = Opt(rgArgs, "--shape") ?? Cfg("LamarrShape");//对外记号是--shape 内部变量名保持LamarrShape
        //--replay的源是--escrow且方向由命令决定 --pack的--escrow才是写
        string? sReplay = sCmd.Equals("--replay", StringComparison.OrdinalIgnoreCase)
            ? (Opt(rgArgs, "--escrow") ?? Cfg("LamarrReplay"))
            : null;//用清单里的材料重打包
        if (!string.IsNullOrEmpty(sSeedHex) && bKeyGiven)
        {
            Console.Error.WriteLine("[shape] --shape and --key/--key-file are mutually exclusive (K = identity/sealing, --shape = shape only)");
            return iErrUsage;
        }
        if (!string.IsNullOrEmpty(sReplay) && bKeyGiven)
        {
            Console.Error.WriteLine("[replay] --key/--key-file must not be combined with a replay: a reproduction needs no K (pass K only for --pack)");
            return iErrUsage;
        }
        bool bShapeOnly = !string.IsNullOrEmpty(sSeedHex) || Opt(rgArgs, "--of-key") != null;//形状源供内部动词用 与K无关
        byte[] rgBuildKey = Array.Empty<byte>();//不能预分配32字节 否则Length为0的判断失效 --key的hex会被静默忽略
        try
        {
            if (!string.IsNullOrEmpty(sKeyFileArg))
            {
                if (!File.Exists(sKeyFileArg)) { Console.Error.WriteLine($"[key] key file not found: {sKeyFileArg}"); return iErrUsage; }
                if (!string.IsNullOrEmpty(sKeyHex)) { Console.Error.WriteLine("[key] --key and --key-file are mutually exclusive (pass exactly one)"); return iErrUsage; }//--key与--key-file互斥 只许给一个
                rgBuildKey = KeyFile.Load(sKeyFileArg);//带LAMARR-KEY标签的钥匙文件 兼容裸hex
            }
            if (bKeyGiven)
            {
                if (rgBuildKey.Length == 0)
                {
                    rgBuildKey = Convert.FromHexString(sKeyHex!.Replace("-", "").Replace(" ", ""));
                    if (rgBuildKey.Length != 32) { Console.Error.WriteLine("[key] --key/--key-file must be 32 bytes (64 hex chars)"); return iErrUsage; }
                }
            }
            else
            {
                rgBuildKey = new byte[32];//必须先分配再填 空数组上直接GetBytes一个字节都写不进去
                using var oRng = System.Security.Cryptography.RandomNumberGenerator.Create();
                oRng.GetBytes(rgBuildKey);
            }
            //K必须是32字节且非全零 全零就一定没解析成功 硬报错不静默出产物
            if (rgBuildKey.Length != 32 || Array.TrueForAll(rgBuildKey, b => b == 0))
            { Console.Error.WriteLine("[key] invalid K (must be 32 non-zero bytes; check --key/--key-file)"); return iErrUsage; }
            //K指纹16hex不泄密 每次构建打一行且走stdout 因为stderr会让Stop偏好的脚本中止
            Console.WriteLine($"[key] src={(bKeyGiven ? (!string.IsNullOrEmpty(sKeyFileArg) ? "key-file" : "key") : (bShapeOnly ? "none" : "random"))} fp={Sha512.Fp(rgBuildKey)}{(!bKeyGiven && bShapeOnly ? " (shape-only input: this K is unused)" : "")}");
        }
        catch (Exception eKey) { Console.Error.WriteLine($"[key] {eKey.Message}"); return iErrUsage; }
        //只有--pack与--replay需要K 内部动词与VM自检只用形状种子 警告不得写stderr 否则Stop偏好的脚本会终止
        bool bPackCmd = sCmd.Equals("--pack", StringComparison.OrdinalIgnoreCase) || sCmd.Equals("--replay", StringComparison.OrdinalIgnoreCase);
        if (bPackCmd && !bKeyGiven && string.IsNullOrEmpty(sReplay) && !bShapeOnly)
            Console.Error.WriteLine($"[key] WARN no --key/--key-file: K=random fp={Convert.ToHexString(Sha512.Hash(rgBuildKey))[..16]} -> this artifact is NOT reproducible unless an escrow is written");
        if (bKeyGiven)
        {
            uSeed = PeWriterAntheil.Kdf64(rgBuildKey, "chain:vmseed");
            A0.Q.uDomainSeed = PeWriterAntheil.DomainSeedOf(uSeed);
        }//自选释放种子要留痕 否则无法复现
        if (!string.IsNullOrEmpty(sSeedHex))
        {
            try { uSeed = ParseSeedHex(sSeedHex); }
            catch { Console.Error.WriteLine($"[shape] bad --shape (want 16 hex chars, e.g. the escrow's vmSeed): {sSeedHex}"); return iErrUsage; }
            Console.WriteLine($"[shape] S={uSeed:X16} (shape-only input: ISA/engine/slots; no K involved)");
        }
        //内部动词的形状也可以直接由K派生 --of-key与--internal shape同一算路
        string? sOfKeyArg = Opt(rgArgs, "--of-key");
        if (!string.IsNullOrEmpty(sOfKeyArg))
        {
            try
            {
                byte[] rgOfK = Convert.FromHexString(sOfKeyArg.Replace("-", "").Replace(" ", "").Trim());
                if (rgOfK.Length != 32) throw new InvalidOperationException("want 64 hex chars");
                uSeed = PeWriterAntheil.Kdf64(rgOfK, "chain:vmseed");
                A0.Q.uDomainSeed = PeWriterAntheil.DomainSeedOf(uSeed);
                Console.WriteLine($"[shape] S={uSeed:X16} (derived from --of-key)");
            }
            catch (Exception eOfKey) { Console.Error.WriteLine("[shape] --of-key " + eOfKey.Message); return iErrUsage; }
        }
        A0.Q.uDomainSeed = PeWriterAntheil.DomainSeedOf(uSeed);//域常量随种子定 解包与期望值核查这些离线动词也要同一份派生
        string? sRole = Opt(rgArgs, "--role");
        int iRole = sRole != null && sRole.Equals("fake", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        string? sOrder = sVerb == "boot-order-check" ? sPos2 : null;//内部动词 对任意boot镜像跑顺序校验
        if (sOrder != null)
        {
            byte[] rgOrder = File.ReadAllBytes(sOrder);
            Console.WriteLine($"[boot-order] image={sOrder} bytes={rgOrder.Length}");
            try { PeWriterAntheil.CheckEncryptOrder(rgOrder); }
            catch (Exception eOrder) { Console.WriteLine($"[boot-order] FAIL {eOrder.Message}"); return iErrCheck; }
            return iOk;
        }

        if (sVerb == "pick-isa")
        {
            Console.WriteLine(IsaPool.PoolPick(uSeed, iRole));//打印所选规范名
            return iOk;
        }
        if (sVerb == "carrier")
        {
            Console.WriteLine(IsaPool.CarrierIsa(uSeed, iRole));//载体ISA池选 没就绪就回退
            return iOk;
        }

        if (sCmd.Equals("--unpack", StringComparison.OrdinalIgnoreCase))//离线还原产物容器与各region
        {
            string? sArtU = Opt(rgArgs, "--artifact");
            if (sArtU == null) { Console.Error.WriteLine("[unpack] needs --artifact <exe> [--out <dir>] [--scan]"); return iErrUsage; }
            if (!File.Exists(sArtU)) { Console.Error.WriteLine($"[unpack] not found: {sArtU}"); return iErrUsage; }
            string sOutU = Opt(rgArgs, "--out") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sArtU))!, "unpack");
            return PeWriterAntheil.Unpack(sArtU, sOutU,
                Array.Exists(rgArgs, a => a.Equals("--scan", StringComparison.OrdinalIgnoreCase)),
                Opt(rgArgs, "--escrow"), Opt(rgArgs, "--escrow-priv") ?? Cfg("LamarrEscrowKey"),
                Opt(rgArgs, "--key") ?? Cfg("LamarrKey"),
                Opt(rgArgs, "--native-out"), Opt(rgArgs, "--stub") ?? Cfg("LamarrStub"),
                Opt(rgArgs, "--boot") ?? Cfg("LamarrBoot"));
        }
        string sCarrierIsa = sIsaName ?? IsaPool.CarrierIsa(uSeed, iRole);//载体ISA需引擎解码与打包端编码都就绪 否则回退ppc
        if (sVerb == "emit-engine")//生成引擎汇编写到文件 输出路径取--out
        {
            string? sEmitEngine = Opt(rgArgs, "--out");
            if (sEmitEngine == null) { Console.Error.WriteLine("[internal emit-engine] needs --out <asm>"); return iErrUsage; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(sEmitEngine))!);
            File.WriteAllText(sEmitEngine, PianolaEngine.Generate(uSeed, sCarrierIsa));
            Console.WriteLine($"[emit-engine] -> {sEmitEngine} (seed={uSeed:X16} carrierIsa={sCarrierIsa})");
            return iOk;
        }
        if (sVerb == "slot-morph")//等长插槽重写 打包器与构建链共用同一实现
        {
            if (sPos2 == null) { Console.Error.WriteLine("[internal slot-morph] needs <dll>"); return iErrUsage; }
            return PeWriterAntheil.SlotMorph(sPos2, Opt(rgArgs, "--out"), uSeed,
                Opt(rgArgs, "--tag") ?? Path.GetFileNameWithoutExtension(sPos2),
                Array.Exists(rgArgs, a => a.Equals("--check", StringComparison.OrdinalIgnoreCase)));
        }
        if (sCmd.Equals("--internal", StringComparison.OrdinalIgnoreCase))//命令级把关 表外动词一律拒绝
        {
            Console.Error.WriteLine($"[internal] unknown verb: {sVerb}");
            return iErrUsage;
        }
        string? sEscrow = sCmd.Equals("--replay", StringComparison.OrdinalIgnoreCase)
            ? Opt(rgArgs, "--escrow-out")//重放时顺带再签一份的输出 可选 复制sigEscrow不重签
            : (Opt(rgArgs, "--escrow") ?? Cfg("LamarrEscrow"));
        string? sEscrowPub = Opt(rgArgs, "--escrow-pub") ?? Cfg("LamarrEscrowPubKey");
        string? sEscrowPriv = Opt(rgArgs, "--escrow-priv") ?? Cfg("LamarrEscrowKey");
        bool bEscrowDiag = Array.Exists(rgArgs, a => a.Equals("--diag", StringComparison.OrdinalIgnoreCase));
        //本次构建的输入指纹 写escrow时记进密封区 重放时用同一算路现算并比对
        //指纹只在要写escrow或要重放比对时才需要 否则不必整读一遍几十MB的载荷
        bool bNeedFp = !string.IsNullOrEmpty(sEscrow) || !string.IsNullOrEmpty(sReplay);
        string sFpPayload = bNeedFp && !string.IsNullOrEmpty(sInput) && File.Exists(sInput) ? PeWriterAntheil.FpOfFile(sInput) : "";
        string sFpNative = bNeedFp ? PeWriterAntheil.FpOfFiles(new[] { sBoot ?? "", sShim ?? "", sDecoder ?? "", sJitHook ?? "", sPheropod ?? "", sEngine ?? "" }) : "";

        string? sEncryptDeps = Opt(rgArgs, "--encrypt-deps") ?? Cfg("EncryptDeps");
        string? sMethodDict = Opt(rgArgs, "--method-dict") ?? Cfg("LamarrMethodDict");
        string? sNoCompressDeps = Opt(rgArgs, "--no-compress-deps") ?? Cfg("NoCompressDeps");
#endif
        string? sRtcVersion = Opt(rgArgs, "--rtc-version") ?? Cfg("RtcVersion");
        string? sTiered = Opt(rgArgs, "--rtc-tiered") ?? Cfg("RtcTiered");
        string? sPayloadMajor = Opt(rgArgs, "--payload-major") ?? Cfg("PayloadMajor");//载荷TFM主版本 它就是stub的PREFMAJ
        int iPayloadMajor = 0;
        if (sPayloadMajor != null) int.TryParse(sPayloadMajor, out iPayloadMajor);
        string? sRollForward = Opt(rgArgs, "--rtc-rollforward") ?? Cfg("RtcRollForward");//null=取载荷值 未发布则用默认策略 取该主版本最新运行时 缺失取更高一级
        string? sTargetArg = Opt(rgArgs, "--target") ?? Cfg("LamarrTarget");
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
                if (!string.IsNullOrEmpty(sSeedHex) && string.IsNullOrEmpty(sReplay))
                {
                    Console.Error.WriteLine("[shape] --shape is a shape-only input (offline verbs / --replay): a fresh pack needs --key (K); to reproduce an artifact pass --replay --escrow <escrow> --escrow-priv <key>");
                    return iErrUsage;
                }
                var pe = new PeWriterAntheil();
                pe.LoadStub(sStub);
                if (!string.IsNullOrEmpty(sMethodDict)) pe.SetMethodDict(sMethodDict);//改名要用它 必须在LoadBoot之前
                pe.SetBuildKey(rgBuildKey);//种子要在LoadBoot之前 LoadBoot里就生成头部并按派生形状
                if (!string.IsNullOrEmpty(sReplay))
                {
                    string sEscJson = PeWriterAntheil.ReadEscrowJson(sReplay, sEscrowPriv);
                    var oEsc = JsonDocument.Parse(sEscJson).RootElement;//用上一次发布的清单重打包
                    string sSeedE = oEsc.TryGetProperty("seed", out var eSd) ? eSd.GetString()! : "";
                    if (sSeedE.Length == 0) { Console.Error.WriteLine("[replay] escrow carries no 'seed': cannot rebuild byte-identically"); return iErrUsage; }
                    pe.SetReplayMaterial(ParseSeedHex(oEsc.GetProperty("vmSeed").GetString()!), Convert.FromHexString(oEsc.GetProperty("head").GetString()!), sSeedE);
                    string sPubBE = oEsc.TryGetProperty("pubB", out var ePb) ? ePb.GetString()! : "";
                    string sSigE = oEsc.TryGetProperty("sig", out var eSg) ? eSg.GetString()! : "";
                    if (sPubBE.Length != 64 || sSigE.Length != 128)
                    {
                        Console.Error.WriteLine("[replay] escrow carries no finished seal (pubB/sig): it was written by an unsealed build");
                        return iErrUsage;
                    }
                    pe.SetReplaySeal(Convert.FromHexString(sPubBE), Convert.FromHexString(sSigE));//重放时复制成品签名 不需要K
                    //escrow的来源签名也必须复制 绝不重签 重签需要K而重放机没有K
                    string sSigEscrowE = oEsc.TryGetProperty(PeWriterAntheil.sEscrowSigField, out var eSe) ? eSe.GetString()! : "";
                    if (sSigEscrowE.Length != 128)
                    {
                        Console.Error.WriteLine("[replay] the source escrow carries no sigEscrow: re-issuing a verifiable escrow needs its signature (regenerate the escrow with the current packer)");
                        return iErrUsage;
                    }
                    pe.SetReplayEscrowSig(sSigEscrowE);
                    //清痕逆材料也原样复制 材料核心必须与源escrow逐字节相同 否则复制的sigEscrow失效
                    pe.SetReplayScrub(oEsc.TryGetProperty("scrub", out var eSc) ? eSc.GetRawText() : "");
                    Console.WriteLine($"[replay] seal copied from escrow (pubB={sPubBE[..16]})");
                    string? sMis = PeWriterAntheil.EscrowVerifyMismatch(sEscJson, sFpPayload, sMode, sFpNative);
                    if (sMis != null)
                    {
                        Console.Error.WriteLine($"[replay] input verification failed: {sMis}");
                        Console.Error.WriteLine("[replay] replaying with different inputs would NOT reproduce the artifact; supply the exact inputs recorded in the escrow");
                        return iErrUsage;
                    }
                    Console.WriteLine("[replay] input fingerprints match the escrow (payload/mode/native)");
                }
                if (sBoot != null) pe.LoadBoot(sBoot);
                if (sShim != null) pe.LoadShim(sShim);
                if (sDecoder != null) pe.LoadDecoder(sDecoder);
                if (sJitHook != null) pe.LoadJitHook(sJitHook);
                pe.LoadPheropod(sPheropod ?? "");
                if (sEngine != null) pe.LoadEngine(sEngine);
                if (sEscrow != null) pe.SetEscrow(sEscrow, sEscrowPub ?? "", bEscrowDiag);
                pe.SetVerifyInfo(sFpPayload, sMode, sFpNative);//写进escrow密封区 重放侧用同一算路比对
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
  tfm={TfmName()} antheil={AntheilFlag()} triage={Triage.Flag()} modes={Modes()}

Usage:
  {sExe}.exe <command> [positionals] [options]     the command decides the direction of --escrow
  {sExe}.exe --configure [options]                 write lamarrpack.props and exit
  {sExe}.exe --version | --help

Commands: --pack --replay --verify --unpack --keygen --key-info --escrow-keygen --escrow-key-info --selftest --internal
Every command only accepts its own options; unknown tokens are rejected (no legacy spellings: section 18.5).

  --pack    --key <64hex> | --key-file <file>         the build key K (**antheil only**)
            --escrow <out> --escrow-pub <pub>         write an escrow (the public key is enough)
            --target <csproj|dir|name>                locate input/output/boot by convention
            --stub --input --output --mode            explicit paths; --mode defaults to managed
            --boot --shim --decoder --jithook --pheropod --engine --method-dict
            --rtc-version --rtc-tiered --rtc-rollforward --payload-major
            --encrypt-deps --no-compress-deps --compress-deps --diag
  --replay  --escrow <in> --escrow-priv <key>         recorded materials (a reproduction needs no K)
            [--escrow-out <out> --escrow-pub <pub>]   also re-issue an escrow (copies sigEscrow)
            (same option set as --pack)
  --verify  --artifact <exe> [--expect <64hex|@file>] without --expect: 'unverified' (self-consistency, not provenance)
  --unpack  --artifact <exe> [--out <dir>] [--scan] [--escrow <in> --escrow-priv <key>] [--key <K>]
            [--native-out <exe> --stub <lamarr_stub.dll> --boot <LamarrBoot.dll>]   also re-pack the recovered payload (lamarr native)
            native re-pack = the native producer path (build-side stub+boot; the artifact's own headers/prefix are the donor)
            ⇒ byte-identical to `--pack --mode native` on the same payload (Lamarr_VM_Design_M1.md §18.16.15)
  --keygen <file> | --key-info <file>                 the K key file (LAMARR-KEY)
  --escrow-keygen <priv> [--pub <pub>] | --escrow-key-info <file>
  --selftest <name>   escrow <dir> | seal <dir> | leak <exe> --key <K>
  --internal <verb>   build-chain internals (scripts call these; section 18.6.8)
            shape --of-key <K> | --of-escrow <escrow> --escrow-priv <key>   print the shape seed S
            pick-isa | carrier [--shape <16hex>] [--role real|fake]
            emit-engine --out <asm> | slot-morph <dll> [--out <dll>] [--tag <t>] [--check]
            boot-order-check <img>");
        Console.Error.WriteLine($@"
Global options:
  --workdir <dir>      work dir, basis for relative paths and discovery
  --config <file>      config file to read (cwd first, then next to the exe)
  --configure          merge explicit args into lamarrpack.props and exit
  --fresh              rewrite lamarrpack.props from scratch (with --configure)
  --out <dir|file>     config location, default next to the tool exe
  --version            print version, tfm, antheil, triage, modes
  --help               print this help
  (auto discovery fills --target/--stub/--boot/--output from the cwd csproj and conventional paths)

Return codes:
  0 ok   1 usage   2 self-check failed   3 mode/command not in this build   4 unknown command   5 exception   6 pub_B mismatch (--expect: not the same source)");
        Console.Error.WriteLine($@"
Examples:
  {sExe}.exe --pack --key-file K.bin --escrow a.escrow --escrow-pub publisher.pub --mode antheil
  {sExe}.exe --replay --escrow a.escrow --escrow-priv priv.pem --escrow-out b.escrow --escrow-pub publisher.pub
  {sExe}.exe --verify --artifact app_packed.exe --expect @publisher.pub
  {sExe}.exe --selftest escrow %TEMP%\escrow_kat
  {sExe}.exe --internal shape --of-key <64hex>
  {sExe}.exe --unpack --artifact app_packed.exe --out unpacked --scan");
        Console.Error.WriteLine();
    }

    static void ShowVersion()
    {
        Console.WriteLine($@"LamarrPacker {Version()}
  tfm     : {TfmName()}
  antheil : {AntheilFlag()}
  triage  : {Triage.Flag()}
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
        bool bFresh = HasFlag(rgArgs, "--fresh");
        var mp = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!bFresh) ReadProps(sPath, mp);
        Put(mp, "Packer", Opt(rgArgs, "--mode"));
        Put(mp, "LamarrTarget", Opt(rgArgs, "--target"));
        Put(mp, "LamarrStub", Opt(rgArgs, "--stub"));
        Put(mp, "LamarrInput", Opt(rgArgs, "--input"));
        Put(mp, "LamarrOutput", Opt(rgArgs, "--output"));
        Put(mp, "LamarrBoot", Opt(rgArgs, "--boot"));
        Put(mp, "RtcVersion", Opt(rgArgs, "--rtc-version"));
        Put(mp, "RtcTiered", Opt(rgArgs, "--rtc-tiered"));
        Put(mp, "RtcRollForward", Opt(rgArgs, "--rtc-rollforward"));
        Put(mp, "PayloadMajor", Opt(rgArgs, "--payload-major"));
        //K绝不写进任何输出 配置表会随产物走
        Put(mp, "LamarrKey", null);
        Put(mp, "LamarrKeyFile", null);
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

    //命令表与每命令封闭的选项集 表外记号一律非法 不留兼容
    static readonly string[] rgCommands = { "--pack", "--replay", "--verify", "--unpack", "--keygen", "--key-info", "--escrow-keygen", "--escrow-key-info", "--selftest", "--internal" };
    static readonly string[] rgGlobOpts = { "--workdir", "--config", "--configure", "--fresh", "--help", "--version", "--seed", "--escrowPlain" };
    static readonly string[] rgPackOpts = { "--target", "--stub", "--input", "--output", "--mode", "--key", "--key-file", "--escrow", "--escrow-pub", "--diag", "--boot", "--shim", "--decoder", "--jithook", "--pheropod", "--engine", "--method-dict", "--rtc-version", "--rtc-tiered", "--rtc-rollforward", "--payload-major", "--encrypt-deps", "--no-compress-deps", "--compress-deps" };
    static readonly string[] rgReplayOpts = { "--target", "--stub", "--input", "--output", "--mode", "--escrow", "--escrow-priv", "--escrow-out", "--escrow-pub", "--diag", "--boot", "--shim", "--decoder", "--jithook", "--pheropod", "--engine", "--method-dict", "--rtc-version", "--rtc-tiered", "--rtc-rollforward", "--payload-major", "--encrypt-deps", "--no-compress-deps", "--compress-deps" };
    static readonly string[] rgVerifyOpts = { "--artifact", "--expect" };
    static readonly string[] rgUnpackOpts = { "--artifact", "--out", "--scan", "--escrow", "--escrow-priv", "--key", "--native-out", "--stub", "--boot" };
    static readonly string[] rgKeygenOpts = { };
    static readonly string[] rgEscrowKeygenOpts = { "--pub" };
    static readonly string[] rgSelftestOpts = { "--key" };
    static readonly string[] rgInternalOpts = { "--shape", "--of-key", "--of-escrow", "--escrow-priv", "--isa", "--role", "--tag", "--check", "--out" };
    //不带值的开关 其余选项都吃一个值 用于位置参数计数与--语义
    static readonly string[] rgNoValue = { "--scan", "--check", "--diag", "--compress-deps", "--configure", "--fresh", "--help", "--version", "--seed", "--escrowPlain" };
    static bool IsNoValue(string s) => Array.Exists(rgNoValue, a => a.Equals(s, StringComparison.OrdinalIgnoreCase));
    //--internal的封闭动词表 表外即错 且在入口就拒绝
    static readonly string[] rgInternalVerbs = { "shape", "pick-isa", "carrier", "emit-engine", "slot-morph", "boot-order-check" };

    static bool IsCommand(string s) => Array.Exists(rgCommands, a => a.Equals(s, StringComparison.OrdinalIgnoreCase));

    static string[] OptsOf(string sCmd)
    {
        if (sCmd.Equals("--pack", StringComparison.OrdinalIgnoreCase)) return rgPackOpts;
        if (sCmd.Equals("--replay", StringComparison.OrdinalIgnoreCase)) return rgReplayOpts;
        if (sCmd.Equals("--verify", StringComparison.OrdinalIgnoreCase)) return rgVerifyOpts;
        if (sCmd.Equals("--unpack", StringComparison.OrdinalIgnoreCase)) return rgUnpackOpts;
        if (sCmd.Equals("--keygen", StringComparison.OrdinalIgnoreCase) || sCmd.Equals("--key-info", StringComparison.OrdinalIgnoreCase)
            || sCmd.Equals("--escrow-key-info", StringComparison.OrdinalIgnoreCase)) return rgKeygenOpts;
        if (sCmd.Equals("--escrow-keygen", StringComparison.OrdinalIgnoreCase)) return rgEscrowKeygenOpts;
        if (sCmd.Equals("--selftest", StringComparison.OrdinalIgnoreCase)) return rgSelftestOpts;
        if (sCmd.Equals("--internal", StringComparison.OrdinalIgnoreCase)) return rgInternalOpts;
        return rgKeygenOpts;
    }

    static int PosMax(string sCmd)
    {
        if (sCmd.Equals("--keygen", StringComparison.OrdinalIgnoreCase) || sCmd.Equals("--key-info", StringComparison.OrdinalIgnoreCase)
            || sCmd.Equals("--escrow-keygen", StringComparison.OrdinalIgnoreCase) || sCmd.Equals("--escrow-key-info", StringComparison.OrdinalIgnoreCase)) return 1;
        if (sCmd.Equals("--selftest", StringComparison.OrdinalIgnoreCase) || sCmd.Equals("--internal", StringComparison.OrdinalIgnoreCase)) return 2;
        return 0;
    }

    //位置参数必须紧跟命令 裸token才算位置参数
    static string? Pos(string[] rgArgs, int i) => (i + 1 >= 0 && i + 1 < rgArgs.Length && !rgArgs[i + 1].StartsWith("-")) ? rgArgs[i + 1] : null;

    static bool IsName(string? sPos, string sWant) => sPos != null && sPos.Equals(sWant, StringComparison.OrdinalIgnoreCase);

    static string[] Merge(params string[][] rgAll)
    {
        var lst = new List<string>();
        foreach (string[] rg in rgAll) lst.AddRange(rg);
        return lst.ToArray();
    }

    static int Distance(string sA, string sB)//编辑距离 用于给出最接近的合法记号建议
    {
        int[] rgPrev = new int[sB.Length + 1];
        int[] rgCur = new int[sB.Length + 1];
        for (int j = 0; j <= sB.Length; j++) rgPrev[j] = j;
        for (int i = 1; i <= sA.Length; i++)
        {
            rgCur[0] = i;
            for (int j = 1; j <= sB.Length; j++)
            {
                int iCost = sA[i - 1] == sB[j - 1] ? 0 : 1;
                rgCur[j] = Math.Min(Math.Min(rgCur[j - 1] + 1, rgPrev[j] + 1), rgPrev[j - 1] + iCost);
            }
            int[] rgTmp = rgPrev; rgPrev = rgCur; rgCur = rgTmp;
        }
        return rgPrev[sB.Length];
    }

    //选项集封闭与位置参数个数 表外记号即硬报错并给出最接近建议与--help提示
    static int ValidateTokens(string[] rgArgs, string sCmd, int iDash)
    {
        string[] rgAllow = OptsOf(sCmd);
        string[] rgAll = Merge(rgAllow, rgGlobOpts, rgCommands);
        int iEnd = iDash < 0 ? rgArgs.Length : iDash;
        int iPos = 0;
        for (int i = 0; i < iEnd; i++)
        {
            string s = rgArgs[i];
            if (!s.StartsWith("-")) { iPos++; continue; }
            if (IsCommand(s)) continue;//命令本身不吃值
            bool bAllowed = Array.Exists(rgGlobOpts, a => a.Equals(s, StringComparison.OrdinalIgnoreCase))
                         || Array.Exists(rgAllow, a => a.Equals(s, StringComparison.OrdinalIgnoreCase));
            if (bAllowed)
            {
                if (!IsNoValue(s)) i++;//跳过它的值 值不参与位置参数计数
                continue;
            }
            string sGuess = "";
            int iBest = int.MaxValue;
            foreach (string sCand in rgAll)
            {
                int iD = Distance(s, sCand);
                if (iD < iBest) { iBest = iD; sGuess = sCand; }
            }
            Console.Error.WriteLine($"[cli] unknown option for {sCmd}: {s}");
            if (iBest <= 3) Console.Error.WriteLine($"[cli] did you mean {sGuess}?");
            Console.Error.WriteLine("[cli] the option set is closed: see --help");
            return iErrUsage;
        }
        if (iPos > PosMax(sCmd))
        {
            Console.Error.WriteLine($"[cli] too many positional arguments for {sCmd} (max {PosMax(sCmd)})");
            return iErrUsage;
        }
        return iOk;
    }

    static string? Opt(string[] rgArgs, string sName)
    {
        int i = Array.FindIndex(rgArgs, a => a.Equals(sName, StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= rgArgs.Length) return null;
        if (rgArgs[i + 1] == "--" && i + 2 < rgArgs.Length) return rgArgs[i + 2];//--之后一律视为值 便于传以-开头的路径
        string sVal = rgArgs[i + 1];
        return sVal.StartsWith("--") ? null : sVal;//值不能是另一个长开关
    }
}
