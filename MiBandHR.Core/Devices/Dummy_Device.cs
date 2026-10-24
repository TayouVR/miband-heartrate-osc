namespace MiBandHR.Core.Devices {
    public class Dummy_Device : Device {
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private readonly object _stateLock = new();
        private CancellationTokenSource? _workerCts;
        private Task? _workerTask;
        private Task? _disposeTask;
        private bool _disposed;

        public Dummy_Device() {
            Name = "Dummy";
        }

        public override ValueTask DisposeAsync() {
            lock (_stateLock) {
                if (_disposeTask != null) return new ValueTask(_disposeTask);
                _disposed = true;
                _disposeTask = DisposeCoreAsync();
                return new ValueTask(_disposeTask);
            }
        }

        private async Task DisposeCoreAsync() {
            await Task.Yield();
            await DisconnectAsync().ConfigureAwait(false);
        }

        public override Task AuthenticateAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_stateLock) {
                ThrowIfDisposed();
                if (Status is DeviceStatus.OnlineUnauth or DeviceStatus.Authenticating or DeviceStatus.Connecting) {
                    Status = DeviceStatus.OnlineAuth;
                }
            }

            return Task.CompletedTask;
        }

        public override async Task ConnectAsync(CancellationToken cancellationToken = default) {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                lock (_stateLock) {
                    ThrowIfDisposed();
                    if (Status == DeviceStatus.OnlineAuth) return;
                    Status = DeviceStatus.Connecting;
                    Status = DeviceStatus.Authenticating;
                    Status = DeviceStatus.OnlineAuth;
                }
            } finally {
                _lifecycleGate.Release();
            }
        }

        public override async Task DisconnectAsync(CancellationToken cancellationToken = default) {
            CancellationTokenSource? workerCts;
            Task? workerTask;
            lock (_stateLock) {
                workerCts = _workerCts;
                workerTask = _workerTask;
                workerCts?.Cancel();
            }

            await _lifecycleGate.WaitAsync().ConfigureAwait(false);
            try {
                try {
                    if (workerTask != null) {
                        await workerTask.ConfigureAwait(false);
                    }
                } catch (OperationCanceledException) {
                    // Expected while stopping the dummy measurement worker.
                } finally {
                    lock (_stateLock) {
                        if (ReferenceEquals(_workerCts, workerCts)) {
                            _workerCts?.Dispose();
                            _workerCts = null;
                            _workerTask = null;
                        }

                        HeartrateMonitorStarted = false;
                        Heartrate = 0;
                        Status = DeviceStatus.Offline;
                    }
                }
            } finally {
                _lifecycleGate.Release();
            }
        }

        public override async Task StartHeartrateMonitorAsync(bool continuous = false,
            CancellationToken cancellationToken = default) {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                lock (_stateLock) {
                    ThrowIfDisposed();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (HeartrateMonitorStarted) return;
                    if (Status != DeviceStatus.OnlineAuth) {
                        throw new InvalidOperationException("The dummy device must be connected before monitoring starts.");
                    }

                    _workerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    _workerTask = RunFakeHeartrateValueWorkerAsync(_workerCts.Token);
                    HeartrateMonitorStarted = true;
                }
            } finally {
                _lifecycleGate.Release();
            }
        }

        public override async Task StopHeartrateMonitorAsync(CancellationToken cancellationToken = default) {
            Task? workerTask;
            CancellationTokenSource? workerCts;
            lock (_stateLock) {
                workerTask = _workerTask;
                workerCts = _workerCts;
                workerCts?.Cancel();
            }

            await _lifecycleGate.WaitAsync().ConfigureAwait(false);
            try {
                if (workerTask != null) {
                    try {
                        await workerTask.ConfigureAwait(false);
                    } catch (OperationCanceledException) {
                        // Expected while stopping the dummy measurement worker.
                    }
                }

                lock (_stateLock) {
                    if (ReferenceEquals(_workerCts, workerCts)) {
                        _workerCts?.Dispose();
                        _workerCts = null;
                        _workerTask = null;
                    }

                    HeartrateMonitorStarted = false;
                }
            } finally {
                _lifecycleGate.Release();
            }
        }

        private async Task RunFakeHeartrateValueWorkerAsync(CancellationToken cancellationToken) {
            var rnd = new Random();
            try {
                while (true) {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    Heartrate = (ushort)rnd.Next(55, 180);
                }
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                // Expected while stopping the dummy measurement worker.
            }
        }

        private void ThrowIfDisposed() {
            if (_disposed) throw new ObjectDisposedException(nameof(Dummy_Device));
        }
    }
}
