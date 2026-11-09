using System.Text;
using BenchmarkDotNet.Attributes;
using VirtualTerminal.Buffer;

namespace VirtualTerminal.Benchmarks;

[MemoryDiagnoser]
public class CatStreamBenchmark
{
    private byte[] _asciiData = null!;
    private byte[] _utf8Data = null!;
    private TerminalDecoder _decoder = null!;

    [Params(4096, 65536)]
    public int TotalBytes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Generate realistic ASCII terminal output (lines of ~80 chars with \n)
        StringBuilder sbAscii = new();
        while (sbAscii.Length < TotalBytes)
        {
            sbAscii.AppendLine("VirtualTerminal high-performance VT100/VT220/VT520 terminal emulation engine.");
        }
        _asciiData = Encoding.UTF8.GetBytes(sbAscii.ToString()[..TotalBytes]);

        // Generate realistic mixed UTF-8 terminal output (Latin, Cyrillic, CJK, Emoji)
        StringBuilder sbUtf8 = new();
        while (sbUtf8.Length < TotalBytes)
        {
            sbUtf8.AppendLine("VirtualTerminal [Тест] [こんにちは世界] [UTF-8 🚀 Fast-Path] [Élégant]");
        }
        _utf8Data = Encoding.UTF8.GetBytes(sbUtf8.ToString()[..TotalBytes]);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _decoder = new TerminalDecoder(new TerminalScreenBuffer(120, 40));
    }

    [Benchmark(Baseline = true)]
    public void StreamAsciiIn4KBChunks()
    {
        ReadOnlySpan<byte> remaining = _asciiData;
        const int chunkSize = 4096;
        while (!remaining.IsEmpty)
        {
            int sliceSize = Math.Min(remaining.Length, chunkSize);
            _decoder.Write(remaining[..sliceSize]);
            remaining = remaining[sliceSize..];
        }
    }

    [Benchmark]
    public void StreamMixedUtf8In4KBChunks()
    {
        ReadOnlySpan<byte> remaining = _utf8Data;
        const int chunkSize = 4096;
        while (!remaining.IsEmpty)
        {
            int sliceSize = Math.Min(remaining.Length, chunkSize);
            _decoder.Write(remaining[..sliceSize]);
            remaining = remaining[sliceSize..];
        }
    }
}
