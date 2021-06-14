using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Orts.Common.Logging
{
    public class ORTraceListener : TraceListener
    {
        private readonly TextWriter writer;
        private readonly bool errorsOnly;
        private bool lastWrittenFormatted;
        private readonly int[] eventCounts = new int[5];

        public int EventCount(TraceEventType eventType)
        {
            int errorLevel = (int)(Math.Log((int)eventType) / Math.Log(2));
            return (errorLevel < eventCounts.Length) ? eventCounts[errorLevel] : -1;
        }

        public ORTraceListener(TextWriter writer)
            : this(writer, false)
        {
        }

        public ORTraceListener(TextWriter writer, bool errorsOnly)
        {
            this.writer = writer;
            this.errorsOnly = errorsOnly;
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id)
        {
            if ((Filter == null) || Filter.ShouldTrace(eventCache, source, eventType, id, null, null, null, null))
                TraceEventInternal(eventCache, source, eventType, id, "", Array.Empty<object>());
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
        {
            if ((Filter == null) || Filter.ShouldTrace(eventCache, source, eventType, id, message, null, null, null))
                TraceEventInternal(eventCache, source, eventType, id, message, Array.Empty<object>());
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args)
        {
            if ((Filter == null) || Filter.ShouldTrace(eventCache, source, eventType, id, format, args, null, null))
                TraceEventInternal(eventCache, source, eventType, id, format, args ?? Array.Empty<object>());
        }

        private void TraceEventInternal(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, object[] args)
        {
            if (null == eventCache)
                return;
            _ = source;
            _ = id;
            // Convert eventType (an enum) back to an index so we can count the different types of error separately.
            int errorLevel = (int)(Math.Log((int)eventType) / Math.Log(2));
            if (errorLevel < eventCounts.Length)
                eventCounts[errorLevel]++;

            // Event is less important than error (and critical) and we're logging only errors... bail.
            if (eventType > TraceEventType.Error && errorsOnly)
                return;

            StringBuilder output = new StringBuilder();
            if (!lastWrittenFormatted)
            {
                output.AppendLine();
                output.AppendLine();
            }
            output.Append(eventType);
            output.Append(": ");
            if (args.Length == 0)
                output.Append(format);
            else
                output.AppendFormat(CultureInfo.InvariantCulture, format, args);

            // Log exception details if it is an exception.
            if (eventCache.LogicalOperationStack.Contains(LogicalOperationWriteException))
            {
                // Attempt to clean up the stacks; the problem is that the exception stack only goes as far back as the call made inside the try block. We also have access to the
                // full stack to this trace call, which goes via the catch block at the same level as the try block. We'd prefer to have the whole stack, so we need to find the
                // join and stitch the stacks together.
                Exception error = args[0] as Exception;
                StackTrace errorStack = new StackTrace(args[0] as Exception);
                StackFrame errorStackLast = errorStack.GetFrame(errorStack.FrameCount - 1);
                StackTrace catchStack = new StackTrace();
                int catchStackIndex = 0;
                while (catchStackIndex < catchStack.FrameCount && errorStackLast != null && catchStack.GetFrame(catchStackIndex).GetMethod().Name != errorStackLast.GetMethod().Name)
                    catchStackIndex++;
                catchStack = new StackTrace(catchStackIndex < catchStack.FrameCount ? catchStackIndex + 1 : 0, true);

                output.AppendLine(error.ToString());
                output.AppendLine(catchStack.ToString());
            }
            else
            {

                // Only log a stack trace for critical and error levels.
                if ((eventType < TraceEventType.Warning) && (TraceOutputOptions & TraceOptions.Callstack) != 0)
                {
                    output.AppendLine();
                    output.AppendLine(new StackTrace(true).ToString());
                }
            }

            output.AppendLine();
            writer.Write(output.ToString());
            lastWrittenFormatted = true;
        }

        public override void Write(string message)
        {
            if (!errorsOnly)
            {
                writer.Write(message);
                lastWrittenFormatted = false;
            }
        }

        public override void WriteLine(string message)
        {
            if (!errorsOnly)
            {
                writer.WriteLine(message);
                lastWrittenFormatted = false;
            }
        }

        public override void WriteLine(object o)
        {
            if (o is Exception)
            {
                Trace.CorrelationManager.StartLogicalOperation(LogicalOperationWriteException);
                if (o is FatalException)
                    Trace.TraceError("", (o as FatalException).InnerException);
                else
                    Trace.TraceWarning("", o);
                Trace.CorrelationManager.StopLogicalOperation();
            }
            else if (!errorsOnly)
            {
                base.WriteLine(o);
                lastWrittenFormatted = false;
            }
        }

        private static readonly LogicalOperation LogicalOperationWriteException = new LogicalOperation();

        private class LogicalOperation
        {
        }
    }
}
