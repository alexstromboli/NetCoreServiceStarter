using System;
using System.Threading;
using System.Threading.Tasks;

namespace Utils.Tasks
{
	public static class CancellationTokenExtensions
	{
		public static Task WaitAnyCancellationAsync (params CancellationToken[] Tokens)
		{
			if (Tokens == null || Tokens.Length == 0)
			{
				throw new ArgumentException ("At least one cancellation token must be provided.", nameof (Tokens));
			}

			// Return immediately if any token is already canceled
			foreach (var t in Tokens)
			{
				if (t.IsCancellationRequested)
				{
					return Task.CompletedTask;
				}
			}

			var tcs = new TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously);
			var Registrations = new CancellationTokenRegistration[Tokens.Length];

			// Use Interlocked to ensure disposal happens only once
			int Disposed = 0;

			void DisposeAll ()
			{
				if (Interlocked.Exchange (ref Disposed, 1) == 0)
				{
					foreach (var reg in Registrations)
					{
						reg.Dispose ();
					}
				}
			}

			for (int i = 0; i < Tokens.Length; i++)
			{
				var t = Tokens[i];
				Registrations[i] = t.Register (() =>
				{
					if (tcs.TrySetResult ())
					{
						DisposeAll ();
					}
				}, useSynchronizationContext: false);
			}

			// Dispose registrations even if awaited Task is abandoned or fails
			tcs.Task.ContinueWith (_ => DisposeAll (), TaskScheduler.Default);

			return tcs.Task;
		}
	}
}
