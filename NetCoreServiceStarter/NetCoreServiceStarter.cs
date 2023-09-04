using System.Threading;
using System.Runtime.InteropServices;

namespace Utils.NetCoreService.Dual
{
	class NetCoreServiceStarter : Utils.LinuxService.Dual.ServiceStarter
	{
		protected AutoResetEvent areStop;

		public NetCoreServiceStarter ()
		{
			areStop = new AutoResetEvent (false);
			PosixSignalRegistration.Create (PosixSignal.SIGINT, c => areStop.Set ());
		}

		protected override string GetNetStarterPath ()
		{
			return "/usr/bin/dotnet";
		}

		protected override void Wait ()
		{
			areStop.WaitOne ();
		}
	}
}
