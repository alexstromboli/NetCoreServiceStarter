using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;

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
			//p.StartInfo.RedirectStandardError = true;
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
		protected abstract void Wait ();

		protected virtual string GetServiceFilePattern ()
		{
			string NetStarterPath = GetNetStarterPath ();

			return
@"[Service]
WorkingDirectory={1}
ExecStart=" + NetStarterPath + @" {0} --{2}
ExecStop=" + NetStarterPath + @" {0} --stop_by_pid $MAINPID
User={3}
Group={4}

[Install]
WantedBy=multi-user.target
";
		}

		protected virtual void KillProcess (int Pid)
		{
			CallProcess ("/bin/kill", "-SIGINT " + Pid);
		}

		// basic procedure
		public void Run (
				string ServiceTitle,                 // service name for registration and start
				Action<ManualResetEvent, bool> MainProc,        // key procedure, what the app must do
				string[] args,       // command line arguments
				string RunAsUser = null,
				string RunAsGroup = null
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
				Setup (ServiceTitle, RunAsUser, RunAsGroup);
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

			ManualResetEvent mreStop = new ManualResetEvent (false);
			Thread thMain = new Thread (() => MainProc (mreStop, IsInServiceMode));
			thMain.Start ();

			if (IsInServiceMode)
			{
				Wait ();
			}
			else
			{
				Console.ReadLine ();
			}

			mreStop.Set ();
			thMain.Join ();
			mreStop.Dispose ();
			mreStop = null;
			thMain = null;
		}

		protected void Setup (string ServiceTitle, string RunAsUser, string RunAsGroup)
		{
			string ServiceName = ServiceNameForTitle (ServiceTitle);

			string ProcessExeFilePath = Environment.GetCommandLineArgs ()[0];
			string ProcessExeDirPath = Path.GetDirectoryName (ProcessExeFilePath);
			string ServiceSpecFilePath = Path.Combine (ProcessExeDirPath, ServiceName);

			string ServiceFileBody = string.Format (GetServiceFilePattern (),
				ProcessExeFilePath,
				ProcessExeDirPath,
				AsServiceCmdKey,
				RunAsUser,
				RunAsGroup
				)
				.Replace ("\r\n", "\n")
				;

			File.WriteAllText (ServiceSpecFilePath, ServiceFileBody);
			File.Copy (ServiceSpecFilePath, SystemdServiceFilePathFromTitle (ServiceTitle), true);
			string EnableOutput = RunSystemctl ("enable", ServiceTitle);
		}
	}
}
