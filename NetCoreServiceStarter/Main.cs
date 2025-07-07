using System;
using System.Threading;
using System.Threading.Tasks;

namespace Utils
{
	public static class NetCoreServiceStarter
	{
		public static Task Run (
				string ServiceTitle,                 // service name for registration and start
				Func<CancellationToken, bool, Task> MainProc,        // key procedure, what the app must do
				string[] Args,       // command line arguments
				string RunAsUser = null,
				string RunAsGroup = null
			)
		{
			return (new Utils.NetCoreService.Dual.NetCoreServiceStarter ())
				.Run (ServiceTitle, MainProc, Args,
				RunAsUser, RunAsGroup);
		}
	}
}
