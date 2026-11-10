using System.Text;
using BenchmarkDotNet.Attributes;
using VirtualTerminal.Model;

namespace VirtualTerminal.Benchmarks;

[MemoryDiagnoser]
public class WcwidthBenchmark
{
    private static readonly Rune[] AsciiRunes = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_+-=[]{}|;':,./<>? "
        .EnumerateRunes()
        .ToArray();

    private static readonly Rune[] LatinRunes = "ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖØÙÚÛÜÝÞßàáâãäåæçèéêëìíîïðñòóôõöøùúûüýþÿ"
        .EnumerateRunes()
        .ToArray();

    private static readonly Rune[] WideRunes = "你好世界，这是一个虚拟终端性能基准测试。こんにちは世界안녕하세요"
        .EnumerateRunes()
        .ToArray();

    private static readonly Rune[] CombiningRunes = [new Rune(0x0300), new Rune(0x0301), new Rune(0x0302), new Rune(0x0303), new Rune(0x0304)];

    [Benchmark(Baseline = true)]
    public int AsciiLookup()
    {
        int sum = 0;
        foreach (Rune r in AsciiRunes)
        {
            sum += Wcwidth.Of(r);
        }
        return sum;
    }

    [Benchmark]
    public int LatinLookup()
    {
        int sum = 0;
        foreach (Rune r in LatinRunes)
        {
            sum += Wcwidth.Of(r);
        }
        return sum;
    }

    [Benchmark]
    public int WideLookup()
    {
        int sum = 0;
        foreach (Rune r in WideRunes)
        {
            sum += Wcwidth.Of(r);
        }
        return sum;
    }

    [Benchmark]
    public int CombiningLookup()
    {
        int sum = 0;
        foreach (Rune r in CombiningRunes)
        {
            sum += Wcwidth.Of(r);
        }
        return sum;
    }
}
