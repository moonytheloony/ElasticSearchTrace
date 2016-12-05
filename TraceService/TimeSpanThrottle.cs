namespace TraceService
{
    using System;

    /// <summary>
    ///     Allows time-based throttling the execution of a method/delegate. Only one execution per given time span is
    ///     performed.
    /// </summary>
    public class TimeSpanThrottle
    {
        private DateTimeOffset? lastExecutionTime;

        private readonly object lockObject;

        private readonly TimeSpan throttlingTimeSpan;

        public TimeSpanThrottle(TimeSpan throttlingTimeSpan)
        {
            this.throttlingTimeSpan = throttlingTimeSpan;
            this.lockObject = new object();
        }

        public void Execute(Action work)
        {
            lock (this.lockObject)
            {
                var now = DateTimeOffset.UtcNow;
                if ((this.lastExecutionTime != null) && (now - this.lastExecutionTime < this.throttlingTimeSpan))
                {
                    return;
                }

                this.lastExecutionTime = now;
            }
            work();
        }
    }
}