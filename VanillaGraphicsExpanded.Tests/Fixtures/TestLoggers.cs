using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Fixtures;

public sealed class TestLogger : ILogger
{
    public bool TraceLog { get; set; }
    public event LogEntryDelegate? EntryAdded;

    public void ClearWatchers() => EntryAdded = null;

    public void Log(EnumLogType logType, string format, params object[] args) => EntryAdded?.Invoke(logType, format, args);
    public void Log(EnumLogType logType, string message) => EntryAdded?.Invoke(logType, message, Array.Empty<object>());
    public void LogException(EnumLogType logType, Exception e) => EntryAdded?.Invoke(logType, e.ToString(), Array.Empty<object>());

    public void Chat(string format, params object[] args) => Log(EnumLogType.Chat, format, args);
    public void Chat(string message) => Log(EnumLogType.Chat, message);

    public void Event(string format, params object[] args) => Log(EnumLogType.Event, format, args);
    public void Event(string message) => Log(EnumLogType.Event, message);

    public void StoryEvent(string format, params object[] args) => Log(EnumLogType.StoryEvent, format, args);
    public void StoryEvent(string message) => Log(EnumLogType.StoryEvent, message);

    public void Build(string format, params object[] args) => Log(EnumLogType.Build, format, args);
    public void Build(string message) => Log(EnumLogType.Build, message);

    public void VerboseDebug(string format, params object[] args) => Log(EnumLogType.VerboseDebug, format, args);
    public void VerboseDebug(string message) => Log(EnumLogType.VerboseDebug, message);

    public void Debug(string format, params object[] args) => Log(EnumLogType.Debug, format, args);
    public void Debug(string message) => Log(EnumLogType.Debug, message);

    public void Notification(string format, params object[] args) => Log(EnumLogType.Notification, format, args);
    public void Notification(string message) => Log(EnumLogType.Notification, message);

    public void Warning(string format, params object[] args) => Log(EnumLogType.Warning, format, args);
    public void Warning(string message) => Log(EnumLogType.Warning, message);
    public void Warning(Exception e) => LogException(EnumLogType.Warning, e);

    public void Error(string format, params object[] args) => Log(EnumLogType.Error, format, args);
    public void Error(string message) => Log(EnumLogType.Error, message);
    public void Error(Exception e) => LogException(EnumLogType.Error, e);

    public void Fatal(string format, params object[] args) => Log(EnumLogType.Fatal, format, args);
    public void Fatal(string message) => Log(EnumLogType.Fatal, message);
    public void Fatal(Exception e) => LogException(EnumLogType.Fatal, e);

    public void Audit(string format, params object[] args) => Log(EnumLogType.Audit, format, args);
    public void Audit(string message) => Log(EnumLogType.Audit, message);
}
