﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Serilog;

namespace HASS.Agent.Shared.Managers
{
    /// <summary>
    /// Performs powershell-related actions
    /// </summary>
    public static class PowershellManager
    {
        /// <summary>
        /// Execute a Powershell command without waiting for or checking results
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        public static bool ExecuteCommandHeadless(string command) => ExecuteHeadless(command, false);

        /// <summary>
        /// Executes a Powershell script without waiting for or checking results
        /// </summary>
        /// <param name="script"></param>
        /// <returns></returns>
        public static bool ExecuteScriptHeadless(string script) => ExecuteHeadless(script, true);

        private static bool ExecuteHeadless(string command, bool isScript)
        {
            var descriptor = isScript ? "script" : "command";

            try
            {
                var workingDir = string.Empty;
                if (isScript)
                {
                    // try to get the script's startup path
                    var scriptDir = Path.GetDirectoryName(command);
                    workingDir = !string.IsNullOrEmpty(scriptDir) ? scriptDir : string.Empty;
                }

                // find the powershell executable
                var psExec = GetPsExecutable();
                if (string.IsNullOrEmpty(psExec)) return false;

                // prepare the executing process
                var processInfo = new ProcessStartInfo
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    FileName = psExec,
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                // set the right type of arguments
                processInfo.Arguments = isScript ?
                    command
                    : $@"& {{{command}}}";

                Log.Information("[POWERSHELL] Staring process {descriptor}: {command}", descriptor, command);
                
                // launch
                using var process = new Process();
                process.StartInfo = processInfo;
                var start = process.Start();
                
                if (!start)
                {
                    Log.Error("[POWERSHELL] Unable to start process {descriptor}: {command}", descriptor, command);
                    return false;
                }

                var StandardOutput = process.StandardOutput.ReadToEnd();
                if (!string.IsNullOrEmpty(StandardOutput))
                {
                    Log.Information("[POWERSHELL][OUTPUT]\n{StandardOutput}", StandardOutput);
                }
                var StandardError = process.StandardError.ReadToEnd();
                if (!string.IsNullOrEmpty(StandardError))
                {
                    Log.Error("[POWERSHELL][ERROR]\n{StandardError}", StandardError);
                }
                
                // done
                return true;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[POWERSHELL] Fatal error when executing {descriptor}: {command}", descriptor, command);
                return false;
            }
        }

        /// <summary>
        /// Execute a Powershell command, logs the output if it fails
        /// </summary>
        /// <param name="command"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public static bool ExecuteCommand(string command, TimeSpan timeout) => Execute(command, false, timeout);

        /// <summary>
        /// Executes a Powershell script, logs the output if it fails
        /// </summary>
        /// <param name="script"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public static bool ExecuteScript(string script, TimeSpan timeout) => Execute(script, true, timeout);

        private static bool Execute(string command, bool isScript, TimeSpan timeout)
        {
            var descriptor = isScript ? "script" : "command";

            try
            {
                var workingDir = string.Empty;
                if (isScript)
                {
                    // try to get the script's startup path
                    var scriptDir = Path.GetDirectoryName(command);
                    workingDir = !string.IsNullOrEmpty(scriptDir) ? scriptDir : string.Empty;
                }

                // find the powershell executable
                var psExec = GetPsExecutable();
                if (string.IsNullOrEmpty(psExec)) return false;

                // prepare the executing process
                var processInfo = new ProcessStartInfo
                {
                    FileName = psExec,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    WorkingDirectory = workingDir,
                    // set the right type of arguments
                    Arguments = isScript
                        ? $@"& '{command}'"
                        : $@"& {{{command}}}"
                };

                // launch
                using var process = new Process();
                process.StartInfo = processInfo;
                var start = process.Start();

                if (!start)
                {
                    Log.Error("[POWERSHELL] Unable to start processing {descriptor}: {script}", descriptor, command);
                    return false;
                }

                // execute and wait
                process.WaitForExit(Convert.ToInt32(timeout.TotalMilliseconds));

                if (process.ExitCode == 0)
                {
                    // done, all good
                    return true;
                }

                // non-zero exitcode, process as failed
                Log.Error("[POWERSHELL] The {descriptor} returned non-zero exitcode: {code}", descriptor, process.ExitCode);

                var errors = process.StandardError.ReadToEnd().Trim();
                if (!string.IsNullOrEmpty(errors)) Log.Error("[POWERSHELL] Error output:\r\n{output}", errors);
                else
                {
                    var console = process.StandardOutput.ReadToEnd().Trim();
                    if (!string.IsNullOrEmpty(console)) Log.Error("[POWERSHELL] No error output, console output:\r\n{output}", errors);
                    else Log.Error("[POWERSHELL] No error and no console output");
                }

                // done
                return false;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[POWERSHELL] Fatal error when executing {descriptor}: {command}", descriptor, command);
                return false;
            }
        }

        /// <summary>
        /// Executes the command or script, and returns the standard and error output
        /// </summary>
        /// <param name="command"></param>
        /// <param name="timeout"></param>
        /// <param name="output"></param>
        /// <param name="errors"></param>
        /// <returns></returns>
        internal static bool ExecuteWithOutput(string command, TimeSpan timeout, out string output, out string errors)
        {
            output = string.Empty;
            errors = string.Empty;

            try
            {
                // check whether we're executing a script
                var isScript = command.ToLower().EndsWith(".ps1");

                var workingDir = string.Empty;
                if (isScript)
                {
                    // try to get the script's startup path
                    var scriptDir = Path.GetDirectoryName(command);
                    workingDir = !string.IsNullOrEmpty(scriptDir) ? scriptDir : string.Empty;
                }

                // find the powershell executable
                var psExec = GetPsExecutable();
                if (string.IsNullOrEmpty(psExec)) return false;

                // prepare the executing process
                var processInfo = new ProcessStartInfo
                {
                    FileName = psExec,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDir,
                    // attempt to set the right encoding
                    StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                    StandardErrorEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                    // set the right type of arguments
                    Arguments = isScript
                        ? $@"& '{command}'"
                        : $@"& {{{command}}}"
                };

                // execute and wait
                using var process = new Process();
                process.StartInfo = processInfo;

                var start = process.Start();
                if (!start)
                {
                    Log.Error("[POWERSHELL] Unable to begin executing the {type}: {cmd}", isScript ? "script" : "command", command);
                    return false;
                }

                // wait for completion
                var completed = process.WaitForExit(Convert.ToInt32(timeout.TotalMilliseconds));
                if (!completed) Log.Error("[POWERSHELL] Timeout executing the {type}: {cmd}", isScript ? "script" : "command", command);

                // read the streams
                output = process.StandardOutput.ReadToEnd().Trim();
                errors = process.StandardError.ReadToEnd().Trim();

                // dispose of them
                process.StandardOutput.Dispose();
                process.StandardError.Dispose();

                // make sure the process ends
                process.Kill();

                // done
                return completed;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Attempt to locate powershell.exe
        /// </summary>
        /// <returns></returns>
        public static string GetPsExecutable()
        {
            // try regular location
            var psExec = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe");
            if (File.Exists(psExec)) return psExec;

            // try specific
            psExec = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "WindowsPowerShell\\v1.0\\powershell.exe");
            if (File.Exists(psExec)) return psExec;

            // not found
            Log.Error("[POWERSHELL] PS executable not found, make sure you have powershell installed on your system");
            return string.Empty;
        }
    }
}
