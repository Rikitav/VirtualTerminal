using Qt.Quick;

namespace VirtualTerminal.Qt
{
    public class Program
    {
        internal static void Main(string[] args)
        {
            Qml.LoadFromRootModule("Main");
            Qml.WaitForExit();
        }
    }
}
