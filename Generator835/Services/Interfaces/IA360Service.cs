using System.Threading.Tasks;

namespace Edi.Generator835.Services.Interfaces
{
    public interface IA360Service
    {
        /// <summary>
        /// Triggers the A360 Bot with the provided validation errors payload (similar to Appsmith workflow payload).
        /// </summary>
        /// <param name="validationErrorsPayload">The payload to send (will be JSON serialized into botInput).</param>
        /// <returns>Task representing the async operation.</returns>
        Task TriggerBotAsync(object validationErrorsPayload);
    }
}
