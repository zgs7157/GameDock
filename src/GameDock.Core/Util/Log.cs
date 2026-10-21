namespace GameDock.Core.Util;

/// <summary>极简日志：CLI 订阅输出到控制台，GUI 订阅输出到界面。</summary>
public static class Log
{
    private static readonly object Gate = new();

    /// <summary>事件参数：消息与级别。</summary>
    public sealed class LogEventArgs(string level, string message) : EventArgs
    {
        public string Level { get; } = level;
        public string Message { get; } = message;
        public DateTime Time { get; } = DateTime.Now;
    }

    public static event EventHandler<LogEventArgs>? Message;

    public static void Info(string msg) => Emit("INFO", msg);
    public static void Warn(string msg) => Emit("WARN", msg);
    public static void Error(string msg) => Emit("ERROR", msg);
    public static void Debug(string msg) => Emit("DEBUG", msg);

    private static void Emit(string level, string msg)
    {
        lock (Gate)
        {
            Message?.Invoke(null, new LogEventArgs(level, msg));
        }
    }
}
