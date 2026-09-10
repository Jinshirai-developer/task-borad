using System.IO;

namespace TaskBoard.Windows;

internal static class Preferences
{
    internal static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskBoard");
}
