namespace VirtualTerminal.Engine;

public class InvalidByteException(byte invalidByte, string message) : Exception(message)
{
    public byte InvalidByte { get; } = invalidByte;
}

public class InvalidCommandException(byte invalidByte, string parameter)
    : InvalidByteException(invalidByte, string.Format("Invalid command {0:X2} '{1}', parameter = \"{2}\"", invalidByte, (char)invalidByte, parameter))
{
    public byte Command => InvalidByte;
    public string Paramter { get; } = parameter;
}

public class InvalidParameterException(byte invalidByte, string parameter)
    : InvalidByteException(invalidByte, string.Format("Invalid parameter for command {0:X2} '{1}', parameter = \"{2}\"", invalidByte, (char)invalidByte, parameter))
{
    public byte Command => InvalidByte;
    public string Paramter { get; } = parameter;
}
