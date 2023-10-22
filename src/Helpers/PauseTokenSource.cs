using System.Threading;
using System.Threading.Tasks;

namespace FilesScanner.Helpers;

public class PauseTokenSource {
    volatile TaskCompletionSource<bool> _paused;
    public bool IsPaused {
        get => _paused != null;
        set {
            if (value) {
                Interlocked.CompareExchange(ref _paused, new TaskCompletionSource<bool>(), null);
            } else {
                while (true) {
                    var completionSource = _paused;
                    if (completionSource == null) {
                        return;
                    }
                    if (Interlocked.CompareExchange(ref _paused, null, completionSource) != completionSource) {
                        continue;
                    }

                    completionSource.SetResult(true);
                    break;
                }
            }
        }
    }
    public Task WaitWhilePausedAsync() {
        var source = _paused;
        return source?.Task ?? Task.CompletedTask;
    }
}