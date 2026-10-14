// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NeoCompose.Runtime.Json;
namespace NeoCompose.Runtime.NeoScript
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum NeoScriptDebugSeverity
    {
        [EnumMember(Value = "log")] Log,
        [EnumMember(Value = "warning")] Warning,
        [EnumMember(Value = "error")] Error,
        [EnumMember(Value = "assert")] Assert,
    }
    [JsonConverter(typeof(StringEnumConverter))]
    public enum NeoScriptCoordinateSpace
    {
        [EnumMember(Value = "body")] Body,
        [EnumMember(Value = "file")] File,
    }
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
        public NeoScriptCoordinateSpace CoordinateSpace
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
        internal NeoScriptStackFrame(NeoScriptSourceInfo? source, long position)
        {
            Name = source?.name ?? "<body>";
            Uri = source?.uri;
            CoordinateSpace = source?.coordinateSpace ?? NeoScriptCoordinateSpace.Body;
            bool positioned = position != NeoScriptTraceState.NoPosition;
            Line = positioned ? NeoScriptTraceState.Line(position) : null;
            Column = positioned ? NeoScriptTraceState.Column(position) : null;
        }
    }
    public sealed class NeoScriptDebugEvent
    {
        public NeoScriptDebugSeverity Severity
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
        internal NeoScriptDebugEvent(NeoScriptDebugSeverity severity, string message, IReadOnlyList<NeoScriptStackFrame> frames)
        {
            Severity = severity;
            Message = message;
            Frames = frames;
        }
    }
    public static class NeoScriptDebug
    {
        public static string FormatError(Exception error)
        {
            string trace = error.Data["neoScriptFrames"] is IReadOnlyList<NeoScriptStackFrame> frames ? FormatFrames(frames) : "";
            return trace.Length == 0 ? error.Message : error.Message + "\n" + trace;
        }
        public static string FormatFrames(IReadOnlyList<NeoScriptStackFrame> frames)
        {
            var text = new StringBuilder();
            foreach (NeoScriptStackFrame frame in frames)
            {
                if (frame.Name == "<body>" && frame.Uri is null && frame.Line is null && frame.Column is null)
                    continue;
                if (text.Length != 0)
                    text.Append('\n');
                text.Append("  NeoScript ").Append(frame.Name).Append(" (").Append(frame.Uri ?? (frame.CoordinateSpace == NeoScriptCoordinateSpace.File ? "file" : "body"));
                if (frame.Line is int line)
                    text.Append(':').Append(line);
                if (frame.Column is int column)
                    text.Append(':').Append(column);
                text.Append(')');
            }
            return text.ToString();
        }
        public static void UnitySink(NeoScriptDebugEvent value)
        {
            string text = "[NeoScript] " + value.Message + "\n" + FormatFrames(value.Frames);
            if (value.Severity == NeoScriptDebugSeverity.Error || value.Severity == NeoScriptDebugSeverity.Assert)
                UnityEngine.Debug.LogError(text);
            else if (value.Severity == NeoScriptDebugSeverity.Warning)
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
                _ when type == MemberKind.Set => value is Array a ? $"<Set count={a.Length}>" : "<Set count=?>",
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
        /// <summary>A frame with no position.</summary>
        internal const long NoPosition = long.MinValue;

        // Every source-bearing expression saves, sets and restores its
        // frame's position. Packing line and column into a long keeps those
        // stores free of references, each of which would cost Mono a GC
        // write barrier.
        private (NeoScriptSourceInfo? source, long position)[] frames = new (NeoScriptSourceInfo?, long)[8];
        private int depth;
        internal static long Pack(NeoScriptSourcePosition? position) =>
            position is null
                ? NoPosition
                : ((long)position.line << 32) | (uint)position.column;
        internal static int Line(long position) => (int)(position >> 32);
        internal static int Column(long position) => (int)position;
        internal (NeoScriptSourceInfo? source, long position)[] Capture()
        {
            var captured = new (NeoScriptSourceInfo?, long)[depth];
            Array.Copy(frames, captured, depth);
            return captured;
        }
        internal T RestoreDuring<T>((NeoScriptSourceInfo? source, long position)[] captured, Func<T> action)
        {
            var previous = frames;
            int previousDepth = depth;
            frames = new (NeoScriptSourceInfo?, long)[Math.Max(8, captured.Length)];
            Array.Copy(captured, frames, captured.Length);
            depth = captured.Length;
            try
            {
                return action();
            }
            catch (Exception error) when (Attach(error)) { throw; }
            finally { frames = previous; depth = previousDepth; }
        }
        internal void Push(FunctionWithReturnType body)
        {
            if (depth == frames.Length)
                Array.Resize(ref frames, depth * 2);
            ref var frame = ref frames[depth++];
            if (!ReferenceEquals(frame.source, body.source))
                frame.source = body.source;
            frame.position = body.instructions.Length == 0 ? NoPosition : Pack(body.instructions[0].source);
        }
        internal void Pop() => depth--;
        internal int Depth => depth;
        internal void RestoreDepth(int savedDepth) => depth = savedDepth;
        internal T Complete<T>(T value)
        {
            depth--;
            return value;
        }
        internal long CurrentPosition => depth == 0 ? NoPosition : frames[depth - 1].position;
        internal void Position(NeoScriptSourcePosition? position) => Position(Pack(position));
        internal void Position(long position)
        {
            if (depth != 0)
                frames[depth - 1].position = position;
        }
        internal IReadOnlyList<NeoScriptStackFrame> Snapshot()
        {
            var result = new NeoScriptStackFrame[Math.Min(64, depth)];
            for (int i = 0; i < result.Length; i++)
            {
                var frame = frames[depth - 1 - i];
                result[i] = new NeoScriptStackFrame(frame.source, frame.position);
            }
            if (depth > 64)
                result[63] = new NeoScriptStackFrame(new NeoScriptSourceInfo { name = "[trace truncated]" }, NoPosition);
            return Array.AsReadOnly(result);
        }
        // Returning false observes an exception without catch/rethrow on Mono's hot paths.
        internal bool Attach(Exception error)
        {
            if (error is NeoFunctionCallSuspended suspended)
                suspended.Execution = suspended.Execution.WithTrace(this);
            else if (!error.Data.Contains("neoScriptFrames"))
                error.Data["neoScriptFrames"] = Snapshot();
            return false;
        }
        internal bool Attach(Exception error, NeoScriptSourcePosition? position)
        {
            if (!error.Data.Contains("neoScriptFrames"))
                Position(position);
            return Attach(error);
        }
    }
}
