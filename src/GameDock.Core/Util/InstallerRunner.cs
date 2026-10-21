using System.Diagnostics;

namespace GameDock.Core.Util;

/// <summary>运行 Java 安装器（Forge / NeoForge / OptiFine 均为 jar 安装器）。</summary>
public static class InstallerRunner
{
    public sealed record RunResult(bool Success, int ExitCode, string Output);

    /// <summary>使用给定 Java 运行安装器，等待退出并回传输出。</summary>
    public static async Task<RunResult> RunJavaAsync(
        string javaPath,
        string installerPath,
        IReadOnlyList<string> args,
        CancellationToken ct,
        Action<string>? onOutput = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = javaPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installerPath);
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        var output = new System.Text.StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.AppendLine(e.Data); } onOutput?.Invoke(e.Data); } };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.AppendLine(e.Data); } onOutput?.Invoke(e.Data); } };

        if (!p.Start())
            return new RunResult(false, -1, "无法启动 Java 进程");

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync(ct);
        return new RunResult(p.ExitCode == 0, p.ExitCode, output.ToString());
    }
}
