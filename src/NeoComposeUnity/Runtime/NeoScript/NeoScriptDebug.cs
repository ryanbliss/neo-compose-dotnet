// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NeoCompose.Runtime.Json;
namespace NeoCompose.Runtime.Json
{
    public sealed class NeoScriptSourceInfo
    {
        public string name = "<body>";
        public string? uri;
        public string coordinateSpace = "body";
    }
    public sealed class NeoScriptSourcePosition
    {
        public int line;
        public int column;
    }
}
namespace NeoCompose.Runtime.NeoScript
{
    public readonly struct NeoScriptStackFrame
    {
        public string Name
        {
            get;
        }
        public string? Uri
        {
            get;
        }
        public string CoordinateSpace
        {
            get;
        }
        public int? Line
        {
            get;
        }
        public int? Column
        {
            get;
        }
        internal NeoScriptStackFrame(NeoScriptSourceInfo? source, NeoScriptSourcePosition? position)
        {
            Name = source?.name ?? "<body>";
            Uri = source?.uri;
            CoordinateSpace = source?.coordinateSpace ?? "body";
            Line = position?.line;
            Column = position?.column;
        }
    }
    public sealed class NeoScriptDebugEvent
    {
        public string Severity
        {
            get;
        }
        public string Message
        {
            get;
        }
        public IReadOnlyList<NeoScriptStackFrame> Frames
        {
            get;
        }
        internal NeoScriptDebugEvent(string severity, string message, IReadOnlyList<NeoScriptStackFrame> frames)
        {
            Severity = severity;
            Message = message;
            Frames = frames;
        }
    }
    public static class NeoScriptDebug
    {
        public static string FormatFrames(IReadOnlyList<NeoScriptStackFrame> frames)
        {
            var text = new StringBuilder();
            foreach (NeoScriptStackFrame frame in frames)
            {
                if (text.Length != 0)
                    text.Append('\n');
                text.Append("  at ").Append(frame.Name).Append(" (").Append(frame.Uri ?? frame.CoordinateSpace);
                if (frame.Line is int line)
                    text.Append(':').Append(line).Append(':').Append(frame.Column ?? 1);
                text.Append(')');
            }
            return text.ToString();
        }
        public static void UnitySink(NeoScriptDebugEvent value)
        {
            string text = "[NeoScript] " + value.Message + "\n" + FormatFrames(value.Frames);
            if (value.Severity == "error" || value.Severity == "assert")
                UnityEngine.Debug.LogError(text);
            else if (value.Severity == "warning")
                UnityEngine.Debug.LogWarning(text);
            else
                UnityEngine.Debug.Log(text);
        }
        private static string FormatFloat(double value)
        {
            if (double.IsNaN(value))
                return "NaN";
            if (double.IsPositiveInfinity(value))
                return "Infinity";
            if (double.IsNegativeInfinity(value))
                return "-Infinity";
            if (value == 0)
                return "0";
            string[] parts = value.ToString("E8", CultureInfo.InvariantCulture).Split('E');
            int exponent = int.Parse(parts[1], CultureInfo.InvariantCulture);
            string sign = value < 0 ? "-" : "";
            string digits = parts[0].Replace("-", "").Replace(".", "").TrimEnd('0');
            if (exponent < -4 || exponent >= 9)
                return sign + digits[0] + (digits.Length > 1 ? "." + digits.Substring(1) : "") + "e" + (exponent >= 0 ? "+" : "") + exponent.ToString(CultureInfo.InvariantCulture);
            int point = exponent + 1;
            if (point <= 0)
                return sign + "0." + new string('0', -point) + digits;
            if (point >= digits.Length)
                return sign + digits + new string('0', point - digits.Length);
            return sign + digits.Substring(0, point) + "." + digits.Substring(point);
        }

        internal static string FormatValue(object? value, MemberKind type)
        {
            string text = value switch
            {
                null => "null",
                string s => s,
                bool b => b ? "true" : "false",
                double d => type == MemberKind.Float ? FormatFloat(d) : d.ToString("G", CultureInfo.InvariantCulture),
                float f => FormatFloat(f),
                int i => i.ToString(CultureInfo.InvariantCulture),
                long l => l.ToString(CultureInfo.InvariantCulture),
                _ when type == MemberKind.List => value is Array a ? $"<List count={a.Length}>" : "<List count=?>",
                _ when type == MemberKind.Dictionary => "<Dictionary>",
                _ when type == MemberKind.Class || type == MemberKind.Interface => "<Class>",
                _ when type == MemberKind.NSDelegate => "<Delegate>",
                _ when type == MemberKind.NSAction => "<Action>",
                _ => "<Value>",
            };
            const string marker = "… [truncated]";
            return text.Length > 4096 ? text.Substring(0, 4096 - marker.Length) + marker : text;
        }
    }
    internal sealed class NeoScriptTraceState
    {
        private List<(NeoScriptSourceInfo? source, NeoScriptSourcePosition? position)> frames = new();
        internal (NeoScriptSourceInfo? source, NeoScriptSourcePosition? position)[] Capture() => frames.ToArray();
        internal T RestoreDuring<T>((NeoScriptSourceInfo? source, NeoScriptSourcePosition? position)[] captured, Func<T> action)
        {
            var previous = frames;
            frames = new List<(NeoScriptSourceInfo? source, NeoScriptSourcePosition? position)>(captured);
            try
            {
                return action();
            }
            catch (Exception error) { Attach(error); throw; }
            finally { frames = previous; }
        }
        internal void Push(FunctionWithReturnType body) => frames.Add((body.source, body.instructions.Length == 0 ? null : body.instructions[0].source));
        internal void Pop() => frames.RemoveAt(frames.Count - 1);
        internal NeoScriptSourcePosition? CurrentPosition => frames.Count == 0 ? null : frames[frames.Count - 1].position;
        internal void Position(NeoScriptSourcePosition? position)
        {
            if (frames.Count != 0)
                frames[frames.Count - 1] = (frames[frames.Count - 1].source, position);
        }
        internal IReadOnlyList<NeoScriptStackFrame> Snapshot()
        {
            var result = new NeoScriptStackFrame[Math.Min(64, frames.Count)];
            for (int i = 0; i < result.Length; i++)
            {
                var frame = frames[frames.Count - 1 - i];
                result[i] = new NeoScriptStackFrame(frame.source, frame.position);
            }
            if (frames.Count > 64)
                result[63] = new NeoScriptStackFrame(new NeoScriptSourceInfo { name = "[trace truncated]" }, null);
            return Array.AsReadOnly(result);
        }
        internal void Attach(Exception error)
        {
            if (!error.Data.Contains("neoScriptFrames"))
                error.Data["neoScriptFrames"] = Snapshot();
        }
    }
}
namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        /// <summary>Default sink for new script executions. Null disables output.</summary>
        public Action<NeoScript.NeoScriptDebugEvent>? ScriptDebugSink { get; set; } = NeoScript.NeoScriptDebug.UnitySink;
    }
}
