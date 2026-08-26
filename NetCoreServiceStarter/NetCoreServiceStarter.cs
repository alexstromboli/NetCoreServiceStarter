using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Utils.NetCoreService.Dual
{
	class NetCoreServiceStarter : Utils.LinuxService.Dual.ServiceStarter, System.IDisposable
	{
		protected CancellationTokenSource StopSource;

		// Fields on purpose, and the only reason this class is disposable:
		// PosixSignalRegistration unregisters the handler from its FINALIZER as well as
		// from Dispose, so a registration nobody holds is one collection away from never
		// firing again
		protected PosixSignalRegistration SigTermRegistration;
		protected PosixSignalRegistration SigIntRegistration;

		public NetCoreServiceStarter ()
		{
			StopSource = new CancellationTokenSource ();

			// PosixSignalRegistration lived here before and was reverted in
			// 70093fd, "Removed the use of PosixSignalRegistration to fix issue noticed
			// on Ubuntu 24". it is back deliberately, and it differs from the reverted
			// form in exactly the two ways that made that form misbehave. do not fold
			// either of them away, and do not revert this again without reading both:
			//
			// 1. the registrations are kept alive in fields. the reverted line was
			//    'PosixSignalRegistration.Create (PosixSignal.SIGINT, c => areStop.Set ());'
			//    and threw the returned IDisposable away, so the handler stopped being
			//    invoked as soon as the object was finalized - which is a "works here,
			//    dead on the server" symptom, exactly what the revert was chasing.
			// 2. Context.Cancel = true suppresses the runtime's DEFAULT handling of the
			//    signal. the reverted line never set it, so the default action tore the
			//    process down at the same moment the graceful shutdown it had just
			//    triggered was starting - two shutdowns racing, one of them fatal.
			//
			// this exact form is in production use on Ubuntu 24.04.
			//
			// SIGTERM is the signal that matters, and the one nothing here listened for:
			// systemd, docker, the DigitalOcean App Platform and a plain 'kill' all send
			// it, while Console.CancelKeyPress - the only stop source before this - is a
			// SIGINT/SIGQUIT mechanism and never covered it at all
			SigTermRegistration = PosixSignalRegistration.Create (PosixSignal.SIGTERM, Context =>
			{
				Context.Cancel = true;
				StopSource.Cancel ();
			});

			// SIGINT covers Ctrl+C and the systemd ExecStop path, and replaces the
			// Console.CancelKeyPress subscription that used to live here. caveat, and it
			// is shell behaviour rather than a defect of ours: a non-interactive shell
			// starts its background children with SIGINT set to SIG_IGN, .NET declines to
			// install a handler over an inherited SIG_IGN, and 'kill -INT' from a script
			// is then a no-op against such a process. scripts should send SIGTERM
			SigIntRegistration = PosixSignalRegistration.Create (PosixSignal.SIGINT, Context =>
			{
				Context.Cancel = true;
				StopSource.Cancel ();
			});
		}

		protected override string GetNetStarterPath ()
		{
			return "/usr/bin/dotnet";
		}

		protected override async Task Wait ()
		{
			await Utils.Tasks.CancellationTokenExtensions.WaitAnyCancellationAsync (StopSource.Token);
		}

		public void Dispose ()
		{
			// the handlers must stop firing before anything they touch goes away.
			// StopSource is deliberately NOT disposed: a signal can arrive at any instant,
			// and Cancel () on a disposed source throws on the runtime's signal-handling
			// thread, where nothing can catch it
			SigIntRegistration?.Dispose ();
			SigIntRegistration = null;

			SigTermRegistration?.Dispose ();
			SigTermRegistration = null;
		}
	}
}
