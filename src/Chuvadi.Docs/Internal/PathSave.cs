using System;
using System.IO;
using Chuvadi.Internal;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// Every Docs save to a path (Docs 2.0, D-206), through the helper shared with Chuvadi.Sheets 2.0.1
/// (<see cref="TempFiles"/>):
/// <list type="bullet">
/// <item>the file is written beside the target and replaces it only when the save succeeded, so a
/// failing save never leaves a truncated file or destroys the file being overwritten;</item>
/// <item>an existing target keeps its permission bits on Unix and, on Windows, its attributes and
/// security settings;</item>
/// <item>when the path is a symbolic link to a file, the file it points to is written and the link
/// is kept;</item>
/// <item>a target that cannot be written (read-only, a folder) and a missing folder fail before
/// anything is written, with the target's name in the error.</item>
/// </list>
/// </summary>
internal static class PathSave
{
    public static void Write(string path, Action<Stream> write)
    {
        var target = TempFiles.ResolveTarget(Path.GetFullPath(path));
        TempFiles.ProbeWritable(target);
        var temp = TempFiles.BesideTarget(target);
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                write(stream);
            TempFiles.Commit(temp, target);
        }
        finally
        {
            TempFiles.TryDelete(temp);
        }
    }
}
