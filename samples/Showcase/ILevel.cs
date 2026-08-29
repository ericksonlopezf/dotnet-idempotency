// Copyright © Erickson Lopez. MIT License.
using System.Threading.Tasks;

namespace EricksonLopez.Idempotency.Showcase;

/// <summary>
/// Defines a progressive learning level in the EricksonLopez.Idempotency Showcase.
/// </summary>
public interface ILevel
{
    /// <summary>
    /// Gets the human-readable name of the level.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the pedagogical description and objectives of the level.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Executes the level demonstration asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous demonstration execution.</returns>
    Task ExecuteAsync();
}
