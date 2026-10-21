using Netrunner.Match;

namespace Netrunner.PersistenceProbe;

/// <summary>Child-process fault probe used only by the atomic storage tests.</summary>
public static class Program
{
    public static void Main(string[] args)
    {
        if (args is not [var path, var marker, var stage]) throw new ArgumentException("Expected destination, marker and stage.");
        AtomicFile.Write(path, [3], temporary =>
        {
            bool backup = temporary.Contains(".bak.", StringComparison.Ordinal);
            if (stage == (backup ? "backup-flushed" : "primary-flushed")) WaitForTermination();
        }, path + ".bak");
        if (stage == "published") WaitForTermination();

        void WaitForTermination()
        {
            File.WriteAllText(marker, stage);
            Thread.Sleep(Timeout.Infinite);
        }
    }
}
