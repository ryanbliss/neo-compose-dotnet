// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Diagnostics;
using System.Text;

namespace NeoCompose.Unity.Editor
{
    /// <summary>
    /// Runs a child process and captures its output. Used for OS-native secret
    /// store CLIs (macOS <c>security</c>, Linux <c>secret-tool</c>) where
    /// shelling out is simpler and safer than P/Invoke marshaling.
    /// </summary>
    public static class NeoComposeProcess
    {
        private const int DefaultTimeoutMilliseconds = 15000;

        public readonly struct Result
        {
            public Result(int exitCode, string standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput;
                StandardError = standardError;
            }

            public int ExitCode { get; }
            public string StandardOutput { get; }
            public string StandardError { get; }
        }

        public static Result Run(
            string fileName,
            string[] arguments,
            string? stdin = null,
            int timeoutMilliseconds = DefaultTimeoutMilliseconds)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin != null,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            var output = new StringBuilder();
            var error = new StringBuilder();

            try
            {
                if (!process.Start())
                {
                    return new Result(-1, "", $"Failed to start process '{fileName}'.");
                }
            }
            catch (Exception exception)
            {
                return new Result(-1, "", $"Failed to start process '{fileName}': {exception.Message}");
            }

            if (stdin != null)
            {
                process.StandardInput.Write(stdin);
                process.StandardInput.Close();
            }

            output.Append(process.StandardOutput.ReadToEnd());
            error.Append(process.StandardError.ReadToEnd());

            if (!process.WaitForExit(timeoutMilliseconds))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Best effort; the process may have exited between the wait
                    // and the kill.
                }

                return new Result(-1, output.ToString(), $"Process '{fileName}' timed out.");
            }

            return new Result(process.ExitCode, output.ToString(), error.ToString());
        }

        /// <summary>
        /// Returns true when the given command resolves on the current PATH.
        /// </summary>
        public static bool CommandExists(string command)
        {
            try
            {
                var result = Run("/usr/bin/env", new[] { "which", command });
                return result.ExitCode == 0 && result.StandardOutput.Trim().Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
