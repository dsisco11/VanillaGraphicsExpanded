using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Support.Cache;

/// <summary>Captures logger entries in memory and publishes them to subscribed test observers.</summary>
public sealed class TestLogger : ILogger
{
    #region Public API
    /// <inheritdoc />
    public bool TraceLog { get; set; }

    /// <inheritdoc />
    public event LogEntryDelegate? EntryAdded;

    /// <inheritdoc />
    public void ClearWatchers() => EntryAdded = null;

    /// <inheritdoc />
    public void Log(EnumLogType logType, string format, params object[] args) => EntryAdded?.Invoke(logType, format, args);

    /// <inheritdoc />
    public void Log(EnumLogType logType, string message) => EntryAdded?.Invoke(logType, message, Array.Empty<object>());

    /// <inheritdoc />
    public void LogException(EnumLogType logType, Exception e) => EntryAdded?.Invoke(logType, e.ToString(), Array.Empty<object>());

    #region Chat
    /// <inheritdoc />
    public void Chat(string format, params object[] args) => Log(EnumLogType.Chat, format, args);

    /// <inheritdoc />
    public void Chat(string message) => Log(EnumLogType.Chat, message);

    #endregion

    #region Events
    /// <inheritdoc />
    public void Event(string format, params object[] args) => Log(EnumLogType.Event, format, args);

    /// <inheritdoc />
    public void Event(string message) => Log(EnumLogType.Event, message);

    /// <inheritdoc />
    public void StoryEvent(string format, params object[] args) => Log(EnumLogType.StoryEvent, format, args);

    /// <inheritdoc />
    public void StoryEvent(string message) => Log(EnumLogType.StoryEvent, message);

    #endregion

    #region Build
    /// <inheritdoc />
    public void Build(string format, params object[] args) => Log(EnumLogType.Build, format, args);

    /// <inheritdoc />
    public void Build(string message) => Log(EnumLogType.Build, message);

    #endregion

    #region Diagnostics
    /// <inheritdoc />
    public void VerboseDebug(string format, params object[] args) => Log(EnumLogType.VerboseDebug, format, args);

    /// <inheritdoc />
    public void VerboseDebug(string message) => Log(EnumLogType.VerboseDebug, message);

    /// <inheritdoc />
    public void Debug(string format, params object[] args) => Log(EnumLogType.Debug, format, args);

    /// <inheritdoc />
    public void Debug(string message) => Log(EnumLogType.Debug, message);

    #endregion

    #region User notifications
    /// <inheritdoc />
    public void Notification(string format, params object[] args) => Log(EnumLogType.Notification, format, args);

    /// <inheritdoc />
    public void Notification(string message) => Log(EnumLogType.Notification, message);

    #endregion

    #region Warnings
    /// <inheritdoc />
    public void Warning(string format, params object[] args) => Log(EnumLogType.Warning, format, args);

    /// <inheritdoc />
    public void Warning(string message) => Log(EnumLogType.Warning, message);

    /// <inheritdoc />
    public void Warning(Exception e) => LogException(EnumLogType.Warning, e);

    #endregion

    #region Errors
    /// <inheritdoc />
    public void Error(string format, params object[] args) => Log(EnumLogType.Error, format, args);

    /// <inheritdoc />
    public void Error(string message) => Log(EnumLogType.Error, message);

    /// <inheritdoc />
    public void Error(Exception e) => LogException(EnumLogType.Error, e);

    #endregion

    #region Fatal errors
    /// <inheritdoc />
    public void Fatal(string format, params object[] args) => Log(EnumLogType.Fatal, format, args);

    /// <inheritdoc />
    public void Fatal(string message) => Log(EnumLogType.Fatal, message);

    /// <inheritdoc />
    public void Fatal(Exception e) => LogException(EnumLogType.Fatal, e);

    #endregion

    #region Audit
    /// <inheritdoc />
    public void Audit(string format, params object[] args) => Log(EnumLogType.Audit, format, args);

    /// <inheritdoc />
    public void Audit(string message) => Log(EnumLogType.Audit, message);
    #endregion
    #endregion
}
