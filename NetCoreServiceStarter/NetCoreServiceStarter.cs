using System.Threading;
using System.Threading.Tasks;

namespace Utils.NetCoreService.Dual
{
	class NetCoreServiceStarter : Utils.LinuxService.Dual.ServiceStarter
	{
		protected CancellationTokenSource StopSource;

		public NetCoreServiceStarter ()
		{
			StopSource = new CancellationTokenSource ();

			System.Console.CancelKeyPress += (_, ea) =>
			{
				ea.Cancel = true;
				StopSource.Cancel ();
			};
		}

		protected override string GetNetStarterPath ()
		{
			return "/usr/bin/dotnet";
		}

		protected override async Task Wait ()
		{
			await Utils.Tasks.CancellationTokenExtensions.WaitAnyCancellationAsync (StopSource.Token);
		}
	}
}
