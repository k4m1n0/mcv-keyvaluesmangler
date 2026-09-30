namespace A0;

public enum ExpSlot//boot读取与打包器写入共用这一份顺序值 打包期从最终boot镜像量出写进占位块 源码不放真值
{
    Jit = 0, Dec = 1, Ad = 2, X1 = 3, X3 = 4, X6 = 5,
    KatA = 6, KatB = 7, KatC = 8, Fan = 9,
}

public static class BootExp
{
    public const int SlotCount = 10;
    public const int BlobBytes = SlotCount * 4;
    public const byte Sentinel = 0xA5;//未写入时的哨兵 打包器须整体覆盖 值留在产物=补丁未生效
    public const uint RawKatA = 0x1F3370D1u;//已知答案的原始值 扰动后必须仍是它
    public const uint RawKatB = 0xADE6ED86u;
    public const uint RawKatC = 0xEB7D845Bu;
    public const uint RawK0 = 0x811C9DC5u;//方法体摘要种子 取boot里常量对A^B的异或值 不受等价变异影响
    public const uint RawK1 = 0x000001B3u;
}
