using System.Threading.Tasks;

namespace Edi.Generator835.Services.Interfaces
{
    public interface IAppsmithService
    {
        /// <summary>
        /// Triggers the Appsmith workflow with the provided payload.
        /// </summary>
        /// <param name="payload">The data to send (will be serialized to JSON).</param>
        /// <returns>Task representing the async operation.</returns>
        Task TriggerWorkflowAsync(object payload);
    }
}
