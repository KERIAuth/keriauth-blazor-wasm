
namespace Extension.Services {
    /// <summary>
    /// Generic observer for storage change notifications.
    /// Provides callbacks for OnNext, OnError, and OnCompleted events.
    /// </summary>
    internal sealed class StorageObserver<T> : IObserver<T> {
        private readonly Action<T>? _onNext;
        private readonly Action<Exception>? _onError;
        private readonly Action? _onCompleted;

        public StorageObserver(
            Action<T>? onNext = null,
            Action<Exception>? onError = null,
            Action? onCompleted = null) {
            _onNext = onNext;
            _onError = onError;
            _onCompleted = onCompleted;
        }

        public void OnNext(T value) {
            _onNext?.Invoke(value);
        }

        public void OnError(Exception error) {
            _onError?.Invoke(error);
        }

        public void OnCompleted() {
            _onCompleted?.Invoke();
        }
    }
}