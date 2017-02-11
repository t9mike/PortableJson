using System;
using System.Collections.Generic;

namespace PortableJson.Xamarin
{
    internal static class JsonUtil
    {
        internal static readonly long InitialJavaScriptDateTicks = 621355968000000000;

        #region DateTime conversion from JSON.Net
        private static TimeSpan GetUtcOffset(DateTime dateTime)
        {
#if SILVERLIGHT && !MONOTOUCH
      return TimeZoneInfo.Local.GetUtcOffset(dateTime);
#else
            return TimeZone.CurrentTimeZone.GetUtcOffset(dateTime);
#endif
        }

        private static long ToUniversalTicks(DateTime dateTime)
        {
            if (dateTime.Kind == DateTimeKind.Utc)
                return dateTime.Ticks;

            return ToUniversalTicks(dateTime, GetUtcOffset(dateTime));
        }

        private static long ToUniversalTicks(DateTime dateTime, TimeSpan offset)
        {
            if (dateTime.Kind == DateTimeKind.Utc)
                return dateTime.Ticks;

            long ticks = dateTime.Ticks - offset.Ticks;
            if (ticks > 3155378975999999999L)
                return 3155378975999999999L;

            if (ticks < 0L)
                return 0L;

            return ticks;
        }

        internal static long ConvertDateTimeToJavaScriptTicks(DateTime dateTime)
        {
            return ConvertDateTimeToJavaScriptTicks(dateTime, true);
        }

        internal static long ConvertDateTimeToJavaScriptTicks(DateTime dateTime, bool convertToUtc)
        {
            long ticks = (convertToUtc) ? ToUniversalTicks(dateTime) : dateTime.Ticks;

            return UniversialTicksToJavaScriptTicks(ticks);
        }

        private static long UniversialTicksToJavaScriptTicks(long universialTicks)
        {
            long javaScriptTicks = (universialTicks - InitialJavaScriptDateTicks) / 10000;

            return javaScriptTicks;
        }

        internal static DateTime ConvertJavaScriptTicksToDateTime(long javaScriptTicks)
        {
            DateTime dateTime = new DateTime((javaScriptTicks * 10000) + InitialJavaScriptDateTicks, DateTimeKind.Utc);

            return dateTime;
        }
        #endregion

        internal static KeyValuePair<object, object> KVCastFrom(Object obj)
        {
            var type = obj.GetType();
            var key = type.GetProperty("Key");
            var value = type.GetProperty("Value");
            var keyObj = key.GetValue(obj, null);
            var valueObj = value.GetValue(obj, null);
            return new KeyValuePair<object, object>(keyObj, valueObj);
        }
    }
}
