namespace TraceService
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    using Microsoft.Diagnostics.Tracing;

    internal static class EventDataExtensions
    {
        // Micro-optimization: Enum.ToString() uses type information and does a binary search for the value,
        // which is kind of slow. We are going to to the conversion manually instead.
        private static readonly string[] EventLevelNames =
            {
                "Always", "Critical", "Error", "Warning", "Informational",
                "Verbose"
            };

        private static readonly string HexadecimalNumberPrefix = "0x";

        public static EventData ToEventData(this EventWrittenEventArgs eventSourceEvent)
        {
            var eventData = new EventData
                {
                    ProviderName = eventSourceEvent.EventSource.GetType().FullName,
                    Timestamp = DateTime.UtcNow,
                    EventId = eventSourceEvent.EventId,
                    Level = EventLevelNames[(int)eventSourceEvent.Level],
                    Keywords =
                        HexadecimalNumberPrefix
                        + ((ulong)eventSourceEvent.Keywords).ToString("X16", CultureInfo.InvariantCulture),
                    EventName = eventSourceEvent.EventName
                };

            try
            {
                if (eventSourceEvent.Message != null)
                {
                    // If the event has a badly formatted manifest, the FormattedMessage property getter might throw
                    eventData.Message = string.Format(
                        CultureInfo.InvariantCulture,
                        eventSourceEvent.Message,
                        eventSourceEvent.Payload.ToArray());
                }
            }
            catch
            {
            }

            eventData.Payload = eventSourceEvent.GetPayloadData();

            return eventData;
        }

        private static IDictionary<string, object> GetPayloadData(this EventWrittenEventArgs eventSourceEvent)
        {
            var payloadData = new Dictionary<string, object>();

            if ((eventSourceEvent.Payload == null) || (eventSourceEvent.PayloadNames == null))
            {
                return payloadData;
            }

            var payloadEnumerator = eventSourceEvent.Payload.GetEnumerator();
            var payloadNamesEnunmerator = eventSourceEvent.PayloadNames.GetEnumerator();
            while (payloadEnumerator.MoveNext())
            {
                payloadNamesEnunmerator.MoveNext();
                payloadData.Add(payloadNamesEnunmerator.Current, payloadEnumerator.Current);
            }

            return payloadData;
        }
    }
}