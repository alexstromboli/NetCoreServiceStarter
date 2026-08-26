using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Utils.LinuxService.Dual
{
	abstract class ServiceStarter
	{
		public static readonly string AsServiceCmdKey = "asservice";

		public static string EscapeStringForBash (string Input)
		{
			if (Input == null)
			{
				return null;
			}

			string Quot = Input.Replace ("'", "'\\''");
			string Result = new StringBuilder ()
				.Append ('\'')
				.Append (Quot)
				.Append ('\'')
				.ToString ()
				;

			return Result;
		}

		public static string CallProcess (string FileName, string Arguments, bool ReadOutput = true)
		{
			Process p = new Process ();
			p.StartInfo.FileName = FileName;
			p.StartInfo.Arguments = Arguments;
			p.StartInfo.UseShellExecute = false;
			p.StartInfo.RedirectStandardOutput = ReadOutput;
			p.Start ();
			int Pid = p.Id;

			// Read the output
			string Output = ReadOutput ? p.StandardOutput.ReadToEnd () : "";
			p.WaitForExit ();
			while (Directory.Exists ("/proc/" + Pid))
			{
				Thread.Sleep (1);
			}
			p.Dispose ();

			return Output;
		}

		protected static string ServiceNameForTitle (string ServiceTitle)
		{
			return ServiceTitle + ".service";
		}

		protected static string SystemdServiceFilePathFromTitle (string ServiceTitle)
		{
			return Path.Combine ("/etc/systemd/system", ServiceNameForTitle (ServiceTitle));
		}

		protected static string RunSystemctl (string Command, string ServiceTitle)
		{
			return CallProcess ("systemctl", Command + " " + ServiceNameForTitle (ServiceTitle));
		}

		protected static string RunBash (string Command)
		{
			return CallProcess ("/bin/bash", EscapeStringForBash (Command));
		}

		protected abstract string GetNetStarterPath ();
		protected abstract Task Wait ();

		protected virtual string GetServiceFilePattern (int? AutoRestartOnFailureInSeconds = null)
		{
			string NetStarterPath = GetNetStarterPath ();

			string RestartDirectives = AutoRestartOnFailureInSeconds.HasValue
				? $"\nRestart=on-failure\nRestartSec={AutoRestartOnFailureInSeconds.Value}"
				: "";

			return
@"[Service]
WorkingDirectory={1}
ExecStart=" + NetStarterPath + @" {0} --{2}{5}
ExecStop=" + NetStarterPath + @" {0} --stop_by_pid $MAINPID
User={3}
Group={4}" + RestartDirectives + @"

[Install]
WantedBy=multi-user.target
";
		}

		protected virtual void KillProcess (int Pid)
		{
			// SIGTERM rather than SIGINT: it is what systemd, docker and a bare
			// 'kill' send anyway, and unlike SIGINT it cannot be silently swallowed by a
			// SIG_IGN the target inherited from a non-interactive shell. both signals are
			// handled now, so an old unit file whose ExecStop still sends SIGINT keeps
			// stopping a new binary, and a new unit file keeps stopping an old one
			CallProcess ("/bin/kill", "-SIGTERM " + Pid);
		}

		// basic procedure
		public async Task Run (
				string ServiceTitle,                 // service name for registration and start
				Func<CancellationToken, bool, Task> MainProc,        // key procedure, what the app must do
				string[] args,       // command line arguments
				string RunAsUser = null,
				string RunAsGroup = null,
				int? AutoRestartOnFailureInSeconds = null
			)
		{
			var Args = new Utils.Args.Args (args, true);

			if (Args.GetAndExcludeKey ("setup") != null)
			{
				// install service
				RunAsUser = RunAsUser ?? "root";
				RunAsGroup = RunAsGroup ?? RunAsUser;

				RunSystemctl ("stop", ServiceTitle);
				RunSystemctl ("disable", ServiceTitle);
				string[] AdditionalStartupCmdlineArgs = Args.SkipWhile (a => a != "--").Skip (1).ToArray ();
				Setup (ServiceTitle, RunAsUser, RunAsGroup, AdditionalStartupCmdlineArgs, AutoRestartOnFailureInSeconds);
				return;
			}
			else if (Args.GetAndExcludeKey ("remove") != null)
			{
				// uninstall service
				RunSystemctl ("stop", ServiceTitle);
				RunSystemctl ("disable", ServiceTitle);

				try
				{
					File.Delete (SystemdServiceFilePathFromTitle (ServiceTitle));
				}
				catch
				{
					// tolerate
				}

				return;
			}
			else if (Args.GetAndExcludeKey ("start") != null)
			{
				// start service
				RunSystemctl ("start", ServiceTitle);
				return;
			}
			else if (Args.GetAndExcludeKey ("restart") != null)
			{
				// restart service
				RunSystemctl ("restart", ServiceTitle);
				return;
			}
			else if (Args.GetAndExcludeKey ("stop") != null)
			{
				// stop service
				RunSystemctl ("stop", ServiceTitle);
				return;
			}
			else if (Args.GetAndExcludeKey ("stop_by_pid") != null)
			{
				// stop service by PID
				int Pid = 0;
				if (Args.Count < 1 || !int.TryParse (Args[0], out Pid))
				{
					return;
				}

				KillProcess (Pid);

				// ensure that the process is down before systemd is done with ExecStop
				// Process.WaitForExit doesn't actually wait

				while (Directory.Exists ("/proc/" + Pid))
				{
					Thread.Sleep (10);
				}

				return;
			}

			// run server
			bool IsInServiceMode = Args.GetAndExcludeKey (AsServiceCmdKey) != null;

			using CancellationTokenSource Cancel = new CancellationTokenSource ();
			Task tMain = MainProc (Cancel.Token, IsInServiceMode);

			// One stop source in every mode - a signal, observed by Wait ().
			// started before the branch below so that a signal arriving while MainProc is
			// still starting up is already recorded by the time we get here
			Task tStop = Wait ();

			if (!IsInServiceMode && !Console.IsInputRedirected)
			{
				// interactive run from a terminal: Enter stops the service the way it always
				// did, and now a signal stops it too. Ctrl+C used to be swallowed here - the
				// handler cancelled a token nothing in this branch was watching, so the only
				// ways out were Enter and kill -9.
				//
				// the wait is SYNCHRONOUS - Task.WaitAny, not await - on purpose. at least one
				// caller invokes Run without awaiting the task it returns, and depends on this
				// call blocking for as long as the service runs; the blocking Console.ReadLine ()
				// used to give it exactly that. an await here would hand control straight back
				// to that caller and let the process exit seconds after startup.
				//
				// the reader task stays blocked on stdin for the rest of the process. that is
				// acceptable: it is one background pool thread, it never keeps the process
				// alive, and there is no portable way to interrupt a read already begun
				Task tReadLine = Task.Run (() => Console.ReadLine ());
				Task.WaitAny (tReadLine, tStop, tMain);
			}
			else
			{
				// service mode, or stdin already at EOF because a script, a unit file or a
				// container started us. Console.ReadLine () returns null immediately in that
				// case, which used to stop the service about a second after it started, with
				// status 0 and nothing said about why
				await Task.WhenAny (tStop, tMain);
			}

			// Stop whatever is still running, then observe MainProc. it may have
			// finished or faulted on its own - Kestrel failing to bind is the usual one -
			// and waiting for a stop that will never come is how this ends up as a live
			// process holding no port and serving nothing
			Cancel.Cancel ();
			await tMain;
		}

		protected void Setup (string ServiceTitle, string RunAsUser, string RunAsGroup, string[] AdditionalStartupCmdlineArgs = null, int? AutoRestartOnFailureInSeconds = null)
		{
			string ServiceName = ServiceNameForTitle (ServiceTitle);

			string ProcessExeFilePath = Environment.GetCommandLineArgs ()[0];
			string ProcessExeDirPath = Path.GetDirectoryName (ProcessExeFilePath);
			string ServiceSpecFilePath = Path.Combine (ProcessExeDirPath, ServiceName);

			string ServiceFileBody = string.Format (GetServiceFilePattern (AutoRestartOnFailureInSeconds),
				ProcessExeFilePath,
				ProcessExeDirPath,
				AsServiceCmdKey,
				RunAsUser,
				RunAsGroup,
				AdditionalStartupCmdlineArgs == null ? "" : string.Join ("", AdditionalStartupCmdlineArgs.Select (a => " " + a))
				)
				.Replace ("\r\n", "\n")
				;

			File.WriteAllText (ServiceSpecFilePath, ServiceFileBody);
			File.Copy (ServiceSpecFilePath, SystemdServiceFilePathFromTitle (ServiceTitle), true);
			string EnableOutput = RunSystemctl ("enable", ServiceTitle);
		}
	}
}
