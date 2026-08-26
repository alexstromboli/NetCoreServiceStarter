using System;
using System.Threading;
using System.Threading.Tasks;

namespace Utils
{
	public static class NetCoreServiceStarter
	{
		public static async Task Run (
				string ServiceTitle,                 // service name for registration and start
				Func<CancellationToken, bool, Task> MainProc,        // key procedure, what the app must do
				string[] Args,       // command line arguments
				string RunAsUser = null,
				string RunAsGroup = null,
				int? AutoRestartOnFailureInSeconds = null
			)
		{
			// The starter owns two PosixSignalRegistrations now, so it has to be
			// disposed - and only AFTER the run is over, which is why this method awaits
			// instead of handing the inner task straight back. a 'using' around a RETURNED
			// task would unregister the signal handlers while the service is still running,
			// and the process would go back to ignoring SIGTERM.
			//
			// awaiting also roots the starter: the local lives in this method's state
			// machine, which the caller's own await keeps reachable for the whole run
			using Utils.NetCoreService.Dual.NetCoreServiceStarter Starter =
				new Utils.NetCoreService.Dual.NetCoreServiceStarter ();

			await Starter.Run (ServiceTitle, MainProc, Args,
				RunAsUser, RunAsGroup, AutoRestartOnFailureInSeconds);
		}
	}
}
