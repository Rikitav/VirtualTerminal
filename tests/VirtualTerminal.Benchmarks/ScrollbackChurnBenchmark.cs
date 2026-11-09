using System.Text;
using BenchmarkDotNet.Attributes;
using VirtualTerminal.Buffer;

namespace VirtualTerminal.Benchmarks;

[MemoryDiagnoser]
public class ScrollbackChurnBenchmark
{
    private TerminalScreenBuffer _buffer = null!;
    private TerminalDecoder _decoder = null!;
    private byte[] _tenThousandLines = null!;

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new TerminalScreenBuffer(80, 24, 5000);

        StringBuilder sb = new();
        for (int i = 0; i < 5000; i++)
        {
            sb.Append("Line ");
            sb.Append(i);
            sb.AppendLine(": Log output streaming continuously into VirtualTerminal scrollback buffer.");
        }
        _tenThousandLines = Encoding.UTF8.GetBytes(sb.ToString());
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _decoder = new TerminalDecoder(new TerminalScreenBuffer(80, 24, 5000));
    }

    [Benchmark]
    public void ScrollUpSingleLine5000Times()
    {
        for (int i = 0; i < 5000; i++)
        {
            _buffer.ScrollUp(1);
        }
    }

    [Benchmark]
    public void ScrollUpMultiLineBatch1000Times()
    {
        for (int i = 0; i < 1000; i++)
        {
            _buffer.ScrollUp(5);
        }
    }

    [Benchmark]
    public void Stream5000LinesThroughDecoder()
    {
        _decoder.Write(_tenThousandLines);
    }
}
