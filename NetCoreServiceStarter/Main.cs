using System;
using System.Threading;

namespace Utils
{
	public static class NetCoreServiceStarter
	{
		public static void Run (
				string ServiceTitle,                 // service name for registration and start
				Action<CancellationToken, bool> MainProc,        // key procedure, what the app must do
				string[] Args,       // command line arguments
				string RunAsUser = null,
				string RunAsGroup = null
			)
		{
			(new Utils.NetCoreService.Dual.NetCoreServiceStarter ())
				.Run (ServiceTitle, MainProc, Args,
				RunAsUser, RunAsGroup);
		}
	}
}
