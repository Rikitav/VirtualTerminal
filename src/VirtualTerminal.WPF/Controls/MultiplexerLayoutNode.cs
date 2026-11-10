using System.Windows.Controls;

namespace VirtualTerminal.Controls;

/// <summary>
/// Serializable description of a split multiplexer layout tree node.
/// </summary>
public class MultiplexerLayoutNode
{
    /// <summary>Gets or sets whether this node is a split container or a leaf pane.</summary>
    public bool IsSplit { get; set; }

    /// <summary>Gets or sets the split orientation if this is a split node.</summary>
    public Orientation Orientation { get; set; }

    /// <summary>Gets or sets the split ratio between Child1 and Child2.</summary>
    public double SplitRatio { get; set; } = 0.5;

    /// <summary>Gets or sets the first child branch.</summary>
    public MultiplexerLayoutNode? Child1 { get; set; }

    /// <summary>Gets or sets the second child branch.</summary>
    public MultiplexerLayoutNode? Child2 { get; set; }

    /// <summary>Gets or sets an identifier or tag for the leaf control.</summary>
    public string? PaneId { get; set; }
}
