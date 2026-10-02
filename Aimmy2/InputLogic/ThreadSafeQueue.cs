using System;
using System.Collections.Concurrent;

namespace Aimmy2.InputLogic
{
    /// <summary>
    /// Thread-safe queue wrapper using ConcurrentQueue for synchronization
    /// between mouse hook thread and AI thread without blocking
    /// </summary>
    /// <typeparam name="T">Type of items stored in queue</typeparam>
    public class ThreadSafeQueue<T>
    {
        private readonly ConcurrentQueue<T> _queue = new();

        /// <summary>
        /// Add an item to the queue
        /// </summary>
        public void Enqueue(T item)
        {
            _queue.Enqueue(item);
        }

        /// <summary>
        /// Try to remove and return the item at the beginning of the queue
        /// </summary>
        public bool TryDequeue(out T? item)
        {
            return _queue.TryDequeue(out item);
        }

        /// <summary>
        /// Get the number of items in the queue
        /// </summary>
        public int Count => _queue.Count;

        /// <summary>
        /// Clear all items from the queue
        /// </summary>
        public void Clear()
        {
            while (_queue.TryDequeue(out _)) { }
        }
    }
}
