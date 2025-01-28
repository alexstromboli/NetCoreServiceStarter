using System.Threading;

namespace Utils.NetCoreService.Dual
{
	class NetCoreServiceStarter : Utils.LinuxService.Dual.ServiceStarter
	{
		protected AutoResetEvent areStop;

		public NetCoreServiceStarter ()
		{
			areStop = new AutoResetEvent (false);
			System.Console.CancelKeyPress += (_, ea) =>
			{
				ea.Cancel = true;
				areStop.Set ();
			};
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
