using System;
using System.IO;
class Probe
{
    static void Main(string[] args) { File.WriteAllLines("launch-probe.txt", new[] { Environment.CurrentDirectory }.ConcatArgs(args)); }
}
static class Args
{
    public static string[] ConcatArgs(this string[] first, string[] second)
    {
        var result = new string[first.Length + second.Length]; Array.Copy(first, result, first.Length); Array.Copy(second, 0, result, first.Length, second.Length); return result;
    }
}
