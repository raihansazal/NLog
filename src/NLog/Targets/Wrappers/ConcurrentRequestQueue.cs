﻿// 
// Copyright (c) 2004-2018 Jaroslaw Kowalski <jaak@jkowalski.net>, Kim Christensen, Julian Verdurmen
// 
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without 
// modification, are permitted provided that the following conditions 
// are met:
// 
// * Redistributions of source code must retain the above copyright notice, 
//   this list of conditions and the following disclaimer. 
// 
// * Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution. 
// 
// * Neither the name of Jaroslaw Kowalski nor the names of its 
//   contributors may be used to endorse or promote products derived from this
//   software without specific prior written permission. 
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE 
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE 
// ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE 
// LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR 
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF 
// THE POSSIBILITY OF SUCH DAMAGE.
// 

#if NET4_5 || NET4_0

namespace NLog.Targets.Wrappers
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using NLog.Common;

    /// <summary>
    /// Concurrent Asynchronous request queue based on <see cref="ConcurrentQueue{T}"/>
    /// </summary>
	internal class ConcurrentRequestQueue : AsyncRequestQueueBase
    {
        private readonly ConcurrentQueue<AsyncLogEventInfo> _logEventInfoQueue = new ConcurrentQueue<AsyncLogEventInfo>();

        /// <summary>
        /// Initializes a new instance of the AsyncRequestQueue class.
        /// </summary>
        /// <param name="requestLimit">Request limit.</param>
        /// <param name="overflowAction">The overflow action.</param>
        public ConcurrentRequestQueue(int requestLimit, AsyncTargetWrapperOverflowAction overflowAction)
        {
            RequestLimit = requestLimit;
            OnOverflow = overflowAction;
        }

        public override bool IsEmpty => _logEventInfoQueue.IsEmpty && Interlocked.Read(ref _count) == 0;

        /// <summary>
        /// Gets the number of requests currently in the queue.
        /// </summary>
        /// <remarks>
        /// Only for debugging purposes
        /// </remarks>
        public int Count => (int)_count;
        private long _count;

        /// <summary>
        /// Enqueues another item. If the queue is overflown the appropriate
        /// action is taken as specified by <see cref="AsyncRequestQueueBase.OnOverflow"/>.
        /// </summary>
        /// <param name="logEventInfo">The log event info.</param>
        /// <returns>Queue was empty before enqueue</returns>
        public override bool Enqueue(AsyncLogEventInfo logEventInfo)
        {
            long currentCount = Interlocked.Increment(ref _count);
            if (currentCount > RequestLimit)
            {
                InternalLogger.Debug("Async queue is full");
                switch (OnOverflow)
                {
                    case AsyncTargetWrapperOverflowAction.Discard:
                        {
                            do
                            {
                                if (_logEventInfoQueue.TryDequeue(out var lostItem))
                                {
                                    InternalLogger.Debug("Discarding one element from queue");
                                    currentCount = Interlocked.Decrement(ref _count);
                                    OnLogEventDropped(lostItem.LogEvent);
                                break;
                                }
                                currentCount = Interlocked.Read(ref _count);
                            } while (currentCount > RequestLimit);
                        }
                        break;
                    case AsyncTargetWrapperOverflowAction.Block:
                        {
                            currentCount = WaitForBelowRequestLimit();
                        }
                        break;
                    case AsyncTargetWrapperOverflowAction.Grow:
                        {
                            OnLogEventQueueGrows(currentCount);
                        }
                        break;
                }
            }
            _logEventInfoQueue.Enqueue(logEventInfo);
            return currentCount == 1;
        }

        private long WaitForBelowRequestLimit()
        {
            long currentCount = 0;
            bool lockTaken = false;
            try
            {
                // Attempt to yield using SpinWait
                currentCount = SpinWait(currentCount);

                // If yield did not help, then wait on a lock
                while (currentCount > RequestLimit)
                {
                    if (!lockTaken)
                    {
                        InternalLogger.Debug("Blocking because the overflow action is Block...");
                        Monitor.Enter(_logEventInfoQueue);
                        lockTaken = true;
                        InternalLogger.Trace("Entered critical section.");
                    }
                    else
                    {
                        InternalLogger.Debug("Blocking because the overflow action is Block...");
                        if (!Monitor.Wait(_logEventInfoQueue, 100))
                            lockTaken = false;
                        else
                            InternalLogger.Trace("Entered critical section.");
                    }
                    currentCount = Interlocked.Read(ref _count);
                }

                if (lockTaken)
                {
                    Monitor.PulseAll(_logEventInfoQueue);
                }
            }
            finally
            {
                if (lockTaken)
                    Monitor.Exit(_logEventInfoQueue);
            }

            return currentCount;
        }

        private long SpinWait(long currentCount)
        {
            bool firstYield = true;
            SpinWait spinWait = new SpinWait();
            for (int i = 0; i <= 20; ++i)
            {
                if (spinWait.NextSpinWillYield)
                {
                    if (firstYield)
                        InternalLogger.Debug("Yielding because the overflow action is Block...");
                    firstYield = false;
                }

                spinWait.SpinOnce();
                currentCount = Interlocked.Read(ref _count);
                if (currentCount <= RequestLimit)
                    break;
            }

            return currentCount;
        }

        /// <summary>
        /// Dequeues a maximum of <c>count</c> items from the queue
        /// and adds returns the list containing them.
        /// </summary>
        /// <param name="count">Maximum number of items to be dequeued (-1 means everything).</param>
        /// <returns>The array of log events.</returns>
        public override AsyncLogEventInfo[] DequeueBatch(int count)
        {
            if (_logEventInfoQueue.IsEmpty)
                return Internal.ArrayHelper.Empty<AsyncLogEventInfo>();

            if (_count < count)
                count = Math.Min(count, Count);

            var resultEvents = new List<AsyncLogEventInfo>(count);

            DequeueBatch(count, resultEvents);

            if (resultEvents.Count == 0)
                return Internal.ArrayHelper.Empty<AsyncLogEventInfo>();
            else
                return resultEvents.ToArray();
        }

        /// <summary>
        /// Dequeues into a preallocated array, instead of allocating a new one
        /// </summary>
        /// <param name="count">Maximum number of items to be dequeued</param>
        /// <param name="result">Preallocated list</param>
        public override void DequeueBatch(int count, IList<AsyncLogEventInfo> result)
        {
            bool dequeueBatch = OnOverflow == AsyncTargetWrapperOverflowAction.Block;

            for (int i = 0; i < count; ++i)
            {
                if (_logEventInfoQueue.TryDequeue(out var item))
                {
                    if (!dequeueBatch)
                        Interlocked.Decrement(ref _count);
                    result.Add(item);
                }
                else
                {
                    break;
                }
            }

            if (dequeueBatch)
            {
                bool lockTaken = Monitor.TryEnter(_logEventInfoQueue);    // Try to throttle
                try
                {
                    for (int i = 0; i < result.Count; ++i)
                        Interlocked.Decrement(ref _count);
                    if (lockTaken)
                        Monitor.PulseAll(_logEventInfoQueue);
                }
                finally
                {
                    if (lockTaken)
                        Monitor.Exit(_logEventInfoQueue);
                }
            }
        }

        /// <summary>
        /// Clears the queue.
        /// </summary>
        public override void Clear()
        {
            while (!_logEventInfoQueue.IsEmpty)
                _logEventInfoQueue.TryDequeue(out var _);
            Interlocked.Exchange(ref _count, 0);
        }
    }
}
#endif