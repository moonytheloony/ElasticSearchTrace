namespace TraceService
{
    using System;
    using System.Collections.Generic;

    public class EventData
    {
        public int EventId { get; set; }

        public string EventName { get; set; }

        public string Keywords { get; set; }

        public string Level { get; set; }

        public string Message { get; set; }

        public IDictionary<string, object> Payload { get; set; }

        public string ProviderName { get; set; }

        public DateTimeOffset Timestamp { get; set; }
    }
}