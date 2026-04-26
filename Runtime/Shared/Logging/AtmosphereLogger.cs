using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;

namespace SolarWeb.Pneuma.Logging
{
  public enum LogLevel : byte
  {
    Trace,
    Debug,
    Info,
    Warn,
    Error
  }

  public enum SubjectType : byte
  {
    Global,
    Entity,
    Region,
    Face,
    Cell
  }

  public enum JobLogId : int
  {
    PlasmaDiffusion,
    Breathe,
    MetabolicReactions,
    PlasmaFiltration,
    SimplifiedMetabolism,
    OverlayDiagnostics,
    GridDiffusion,
    ThermalExchange,
    Combustion,
    Topology,
    StateExport,
    Environment,
    GasExchange,
    Plants,
    PhaseTransition,
    UI,
    Sync
  }

  public enum JobLogVarType : byte
  {
    Float,
    Long,
    Int,
    Bool
  }

  /// <summary>
  /// A single unmanaged log entry suitable for high-frequency Burst jobs.
  /// </summary>
  public struct JobLogEvent
  {
    public int Tick;
    public JobLogId JobId;
    public LogLevel Level;
    public SubjectType SubjType;
    public int SubjectIndex;
    public int ContextId; // For per-gas or per-globin logging within a single entity
    public FixedString32Bytes VarName;
    public JobLogVarType Type;
    public float FloatValue;
    public long LongValue;
  }

  /// <summary>
  /// Unmanaged logger struct passed into Burst jobs.
  /// </summary>
  public struct JobLogger
  {
    public NativeQueue<JobLogEvent>.ParallelWriter Queue;
    public bool IsLogging;
    public LogLevel MinLevel;
    public SubjectType TargetSubjType;
    public int TargetSubjectIndex;
    public int Tick;
    public JobLogId JobId;
    public int SampleInterval;

    [Conditional("DEBUG")]
    public void Log(LogLevel level, SubjectType subjType, int subjectIndex, int contextId, in FixedString32Bytes varName, float value)
    {
      if (!IsLogging || level < MinLevel) return;
      if (TargetSubjectIndex >= 0 && (subjType != TargetSubjType || subjectIndex != TargetSubjectIndex)) return;
      if (SampleInterval > 1 && (Tick % SampleInterval != 0)) return;

      Queue.Enqueue(new JobLogEvent
      {
        Tick = Tick,
        JobId = JobId,
        Level = level,
        SubjType = subjType,
        SubjectIndex = subjectIndex,
        ContextId = contextId,
        VarName = varName,
        Type = JobLogVarType.Float,
        FloatValue = value
      });
    }

    [Conditional("DEBUG")]
    public void Log(LogLevel level, SubjectType subjType, int subjectIndex, int contextId, in FixedString32Bytes varName, long value)
    {
      if (!IsLogging || level < MinLevel) return;
      if (TargetSubjectIndex >= 0 && (subjType != TargetSubjType || subjectIndex != TargetSubjectIndex)) return;
      if (SampleInterval > 1 && (Tick % SampleInterval != 0)) return;

      Queue.Enqueue(new JobLogEvent
      {
        Tick = Tick,
        JobId = JobId,
        Level = level,
        SubjType = subjType,
        SubjectIndex = subjectIndex,
        ContextId = contextId,
        VarName = varName,
        Type = JobLogVarType.Long,
        LongValue = value
      });
    }

    [Conditional("DEBUG")]
    public void Log(LogLevel level, SubjectType subjType, int subjectIndex, int contextId, in FixedString32Bytes varName, int value)
    {
      if (!IsLogging || level < MinLevel) return;
      if (TargetSubjectIndex >= 0 && (subjType != TargetSubjType || subjectIndex != TargetSubjectIndex)) return;
      if (SampleInterval > 1 && (Tick % SampleInterval != 0)) return;

      Queue.Enqueue(new JobLogEvent
      {
        Tick = Tick,
        JobId = JobId,
        Level = level,
        SubjType = subjType,
        SubjectIndex = subjectIndex,
        ContextId = contextId,
        VarName = varName,
        Type = JobLogVarType.Int,
        LongValue = value
      });
    }

    [Conditional("DEBUG")]
    public void Log(LogLevel level, SubjectType subjType, int subjectIndex, int contextId, in FixedString32Bytes varName, bool value)
    {
      if (!IsLogging || level < MinLevel) return;
      if (TargetSubjectIndex >= 0 && (subjType != TargetSubjType || subjectIndex != TargetSubjectIndex)) return;
      if (SampleInterval > 1 && (Tick % SampleInterval != 0)) return;

      Queue.Enqueue(new JobLogEvent
      {
        Tick = Tick,
        JobId = JobId,
        Level = level,
        SubjType = subjType,
        SubjectIndex = subjectIndex,
        ContextId = contextId,
        VarName = varName,
        Type = JobLogVarType.Bool,
        LongValue = value ? 1 : 0
      });
    }

    // Backward compatibility for existing metabolism jobs
    [Conditional("DEBUG")]
    public void Log(int entityIndex, int contextId, in FixedString32Bytes varName, float value) =>
      Log(LogLevel.Info, SubjectType.Entity, entityIndex, contextId, varName, value);

    [Conditional("DEBUG")]
    public void Log(int entityIndex, int contextId, in FixedString32Bytes varName, long value) =>
      Log(LogLevel.Info, SubjectType.Entity, entityIndex, contextId, varName, value);

    [Conditional("DEBUG")]
    public void Log(int entityIndex, int contextId, in FixedString32Bytes varName, int value) =>
      Log(LogLevel.Info, SubjectType.Entity, entityIndex, contextId, varName, value);

    [Conditional("DEBUG")]
    public void Log(int entityIndex, int contextId, in FixedString32Bytes varName, bool value) =>
      Log(LogLevel.Info, SubjectType.Entity, entityIndex, contextId, varName, value);
  }

  /// <summary>
  /// Managed orchestrator for simulation logging sessions.
  /// </summary>
  public class AtmosphereJobLogger : IDisposable
  {
    public NativeQueue<JobLogEvent> EventQueue;
    public bool IsLoggingEnabled;
    public LogLevel MinLogLevel = LogLevel.Info;
    public SubjectType TargetSubjType = SubjectType.Global;
    public int TargetSubjectIndex = -1;
    public int SampleInterval = 1;
    public HashSet<JobLogId> ActiveJobs = new();

    public bool IsSessionActive { get; private set; }
    public string LastSessionTargetName { get; private set; }

    public AtmosphereJobLogger()
    {
      EventQueue = new NativeQueue<JobLogEvent>(Allocator.Persistent);
      LastSessionTargetName = "Default";
    }

    public JobLogger GetLogger(JobLogId jobId, int tick)
    {
      return new JobLogger
      {
        Queue = EventQueue.AsParallelWriter(),
        IsLogging = IsLoggingEnabled && ActiveJobs.Contains(jobId),
        MinLevel = MinLogLevel,
        TargetSubjType = TargetSubjType,
        TargetSubjectIndex = TargetSubjectIndex,
        Tick = tick,
        JobId = jobId,
        SampleInterval = SampleInterval
      };
    }

    public void StartLogging(SubjectType targetType, int targetIndex, LogLevel minLevel, string targetName, int sampleInterval, params JobLogId[] jobsToLog)
    {
      IsLoggingEnabled = true;
      IsSessionActive = true;
      TargetSubjType = targetType;
      TargetSubjectIndex = targetIndex;
      MinLogLevel = minLevel;
      LastSessionTargetName = targetName;
      SampleInterval = Math.Max(1, sampleInterval);
      ActiveJobs.Clear();
      foreach (var job in jobsToLog) ActiveJobs.Add(job);

      // Clear residual events from previous sessions
      while (EventQueue.TryDequeue(out _)) { }
    }

    // Legacy overload for backward compatibility
    public void StartLogging(int targetEntityIndex, string targetName, int sampleInterval, params JobLogId[] jobsToLog)
    {
      StartLogging(SubjectType.Entity, targetEntityIndex, LogLevel.Info, targetName, sampleInterval, jobsToLog);
    }

    public void StopLogging()
    {
      IsLoggingEnabled = false;
    }

    public void EndSession()
    {
      StopLogging();
      IsSessionActive = false;
    }

    public void FlushLogsToXml(string path)
    {
      if (!EventQueue.IsCreated) return;

      var events = new List<JobLogEvent>();
      while (EventQueue.TryDequeue(out var ev))
      {
        events.Add(ev);
      }

      if (events.Count == 0) return;

      var sb = new StringBuilder();
      sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
      sb.AppendLine("<JobLogs>");

      // Grouping by high-level context before detailed var-by-var output
      var grouped = events.GroupBy(e => (e.Tick, e.JobId, e.Level, e.SubjType, e.SubjectIndex, e.ContextId))
                .OrderBy(g => g.Key.Tick)
                .ThenBy(g => g.Key.JobId.ToString())
                .ThenBy(g => g.Key.SubjType.ToString())
                .ThenBy(g => g.Key.SubjectIndex)
                .ThenBy(g => g.Key.ContextId);

      foreach (var group in grouped)
      {
        sb.Append($"  <Log tick=\"{group.Key.Tick}\" job=\"{group.Key.JobId}\" lvl=\"{group.Key.Level}\" type=\"{group.Key.SubjType}\" idx=\"{group.Key.SubjectIndex}\" ctx=\"{group.Key.ContextId}\"");

        foreach (var ev in group)
        {
          string valStr = ev.Type switch
          {
            JobLogVarType.Float => ev.FloatValue.ToString("R"),
            JobLogVarType.Long => ev.LongValue.ToString(),
            JobLogVarType.Int => ev.LongValue.ToString(),
            JobLogVarType.Bool => ev.LongValue == 1 ? "true" : "false",
            _ => ""
          };
          sb.Append($" {ev.VarName}=\"{valStr}\"");
        }

        sb.AppendLine(" />");
      }

      sb.AppendLine("</JobLogs>");
      File.WriteAllText(path, sb.ToString());
    }

    public void Dispose()
    {
      if (EventQueue.IsCreated) EventQueue.Dispose();
    }
  }
}
