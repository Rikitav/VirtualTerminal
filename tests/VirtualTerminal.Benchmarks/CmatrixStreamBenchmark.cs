using System.Text;
using BenchmarkDotNet.Attributes;
using VirtualTerminal.Buffer;

namespace VirtualTerminal.Benchmarks;

[MemoryDiagnoser]
public class CmatrixStreamBenchmark
{
    private byte[] _cmatrixFrames = null!;
    private TerminalDecoder _decoder = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Build simulated cmatrix / htop ANSI frame sequence
        StringBuilder sb = new();
        Random rng = new(42);

        for (int frame = 0; frame < 50; frame++)
        {
            // Rapid cursor positioning and green/bright green rain drops
            for (int drop = 0; drop < 30; drop++)
            {
                int row = rng.Next(1, 25);
                int col = rng.Next(1, 81);
                char ch = (char)rng.Next(0x21, 0x7E);
                string color = (rng.Next(2) == 0) ? "\x1b[32m" : "\x1b[1;32m";

                sb.Append($"\x1b[{row};{col}H{color}{ch}");
            }
        }

        _cmatrixFrames = Encoding.UTF8.GetBytes(sb.ToString());
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _decoder = new TerminalDecoder(new TerminalScreenBuffer(80, 24));
    }

    [Benchmark]
    public void StreamCmatrixFrames()
    {
        _decoder.Write(_cmatrixFrames);
    }
}
