using System;
using System.Collections.Generic;
using System.Threading;

namespace Edi.Generator835.Context
{
    /// <summary>
    /// Maintains state across a generation run — control numbers, batch identity, extensible metadata.
    /// Thread-safe for concurrent use within a batch.
    /// </summary>
    public class GenerationContext
    {
        public IControlNumberProvider ControlNumbers { get; }
        public DateTime ProcessingDate { get; }
        public string BatchId { get; }

        /// <summary>
        /// Extensible state bag for rules to read/write computed values.
        /// </summary>
        public Dictionary<string, object> Metadata { get; } = new Dictionary<string, object>();

        public GenerationContext(
            IControlNumberProvider? controlNumberProvider = null,
            DateTime? processingDate = null,
            string? batchId = null)
        {
            ControlNumbers = controlNumberProvider ?? new SequentialControlNumberProvider();
            ProcessingDate = processingDate ?? DateTime.UtcNow;
            BatchId = batchId ?? Guid.NewGuid().ToString("N").Substring(0, 8);
        }
    }

    /// <summary>
    /// Provides unique, incrementing control numbers for ISA, GS, and ST envelopes.
    /// </summary>
    public interface IControlNumberProvider
    {
        string NextInterchangeControlNumber();
        string NextGroupControlNumber();
        string NextTransactionControlNumber();
    }

    /// <summary>
    /// Simple sequential provider. Starts from a configurable seed.
    /// </summary>
    public class SequentialControlNumberProvider : IControlNumberProvider
    {
        private int _interchangeCounter;
        private int _groupCounter;
        private int _transactionCounter;

        public SequentialControlNumberProvider(int startFrom = 1)
        {
            // Initializing to startFrom - 1 because Interlocked.Increment increments BEFORE returning the value
            _interchangeCounter = startFrom - 1;
            _groupCounter = startFrom - 1;
            _transactionCounter = startFrom - 1;
        }

        public string NextInterchangeControlNumber()
        {
            int val = Interlocked.Increment(ref _interchangeCounter);
            return val.ToString().PadLeft(9, '0');
        }

        public string NextGroupControlNumber()
        {
            int val = Interlocked.Increment(ref _groupCounter);
            return val.ToString();
        }

        public string NextTransactionControlNumber()
        {
            int val = Interlocked.Increment(ref _transactionCounter);
            return val.ToString().PadLeft(4, '0');
        }
    }
}
