using System.Text;

namespace VirtualTerminal.Engine.Components;

public interface IDecoder
{
    Encoding Encoding { get; set; }

    /// <summary>
    /// <para>Tell decoder to process the given data.</para>
    /// <para>
    /// If an invalid byte is passed InvalidByteException or one
    /// of the its sub-classes is thrown. The decoder will try its
    /// best to survive any invalid data and should still be able
    /// to process data after an exception is thrown.
    /// </para>
    /// </summary>
    void Write(ReadOnlySpan<byte> data);
}
