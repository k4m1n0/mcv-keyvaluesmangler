namespace Lamarr.Packer;

public static class BootNames
{
    public static readonly string[] Encrypted = { "W1", "X4", "X5", "TCheck", "X2", "X3Comp", "VerifyBodies" };
    public static readonly string[] DictPicked = { "TCheck", "VerifyBodies" };
    public static readonly string[] MoveInjected = { "DeriveSeedN", "RestoreData", "GenDisturbN", "CheckElapsed", "SelfCheck" };
    public static readonly string[] NoiseInjected = { "X1", "X3", "DeriveSeedN", "RestoreData", "GenDisturbN", "CheckElapsed", "SelfCheck", "AD" };
    public static readonly string[] PairFields = {//常量对字段 打包期写成互为异或的两个值 真值取其中一个
        "uLK0A","uLK0B","uLK1A","uLK1B","uLGAA","uLGAB","uLLCA","uLLCB","uLQCA","uLQCB",
        "uKS0A","uKS0B","uKS1A","uKS1B","uKS2A","uKS2B","uKS3A","uKS3B",
        "uQ1A","uQ1B","uQ2A","uQ2B","uQ3A","uQ3B","uQ4A","uQ4B","uR1A","uR1B","uR2A","uR2B",
        "uV1A","uV1B","uV2A","uV2B","uV3A","uV3B","uV4A","uV4B","uV5A","uV5B","uV6A","uV6B","uV7A","uV7B" };
}
