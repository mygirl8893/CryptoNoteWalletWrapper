﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;

namespace CryptoNoteWallet.Core
{
    /// <summary>
    /// Wraps the daemon command line application. Sends command and interprets output.
    /// </summary>
    public class DaemonWrapper : BaseWrapper
    {
        private Timer PingTimer { get; set; }
        private Timer ConnectionTimer { get; set; }
        private int ConnectionCount { get; set; }

        public EventHandler<WrapperEvent<int>> ConnectionsCounted;

        public DaemonWrapper(string walletPath, string exeFileName, int pingInterval, int connectionCountInterval)
            : base(walletPath, exeFileName)
        {
            PingTimer = new Timer(pingInterval);
            PingTimer.Elapsed += (s, e) => Ping();

            ConnectionTimer = new Timer(connectionCountInterval);
            ConnectionTimer.Elapsed += (s, e) => GetConnections();
        }

        public async void Start()
        {
            if (!CanStart())
            {
                return;
            }

            WrapperProcess = new Process();

            var processStartInfo = new ProcessStartInfo(ExecutablePath);
            processStartInfo.UseShellExecute = false;
            processStartInfo.RedirectStandardOutput = true;
            processStartInfo.RedirectStandardInput = true;
            processStartInfo.RedirectStandardError = true;
            processStartInfo.CreateNoWindow = true;
            WrapperProcess.StartInfo = processStartInfo;
            WrapperProcess.Start();

            await Task.Factory.StartNew(() => ReadNextLine(false));
            await Task.Factory.StartNew(() => ReadNextLine(true));

            PingTimer.Start();
            ConnectionTimer.Start();
        }

        /// <summary>
        /// Keep the daemon output up to date by entering a newline.
        /// </summary>
        public void Ping()
        {
            WriteLine(Environment.NewLine);
        }

        /// <summary>
        /// Print a list of connections.
        /// </summary>
        public void GetConnections()
        {
            WriteLine("print_cn");
        }

        public override void Exit()
        {
            HandleLines = false;

            PingTimer.Stop();
            ConnectionTimer.Stop();

            base.Exit();
        }

        /// <summary>
        /// Interpret the current wallet output and call relevant event listeners.
        /// </summary>
        /// <param name="line">Current line.</param>
        /// <param name="isError">Is the line read from StandardError?</param>
        protected override void HandleLine(string line, bool isError)
        {
            bool isCountingConnections = false;

            if (line.Contains("days) behind"))
            {
                Match match = Regex.Match(line, "([0-9]+) blocks\\(([0-9]+) days\\) behind");
                if (match.Success)
                {
                    UpdateStatus(
                        WalletStatus.SynchronizingBlockchain, 
                        string.Format("Retrieving blockchain ({0} blocks / {1} days behind)", match.Groups[1].Value, match.Groups[2].Value));
                }
            }
            else if (Regex.IsMatch(line, "Remote Host[\\s]+Peer id"))
            {
                ConnectionCount = 0;
                isCountingConnections = true;
            }
            else if (Regex.IsMatch(line, "\\[OUT\\][0-9\\.:]+[\\s]+[0-9a-z]+"))
            {
                ConnectionCount++;
                isCountingConnections = true;
            }

            if (!isCountingConnections && ConnectionsCounted != null)
            {
                ConnectionsCounted.Invoke(this, new WrapperEvent<int>(ConnectionCount));
            }

            base.HandleLine(line, isError);
        }
    }
}