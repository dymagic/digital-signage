using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Signage.Web.Domain;

namespace Signage.IntegrationTests.Infrastructure;

/// <summary>
/// The real hasher, except that a verification of a password that has a gate set pauses until the test releases it.
/// It lets tests park a sign-in or password change in the middle of its check and run something else
/// "at the same moment", deterministically and without sleeps.
/// </summary>
public sealed class GatedPasswordHasher : IPasswordHasher<User>
{
    private readonly PasswordHasher<User> _inner = new();
    private readonly ConcurrentDictionary<string, Gate> _gates = new(StringComparer.Ordinal);

    /// <summary>The next verification of <paramref name="password"/> will pause.</summary>
    public Gate Arm(string password) => _gates[password] = new Gate();

    public string HashPassword(User user, string password) => _inner.HashPassword(user, password);

    public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword)
    {
        if (_gates.TryRemove(providedPassword, out var gate))
        {
            gate.Reached.TrySetResult();
            if (!gate.Released.Wait(TimeSpan.FromSeconds(30)))
            {
                throw new TimeoutException("Gate was never released.");
            }
        }

        return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }

    public sealed class Gate : IDisposable
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ManualResetEventSlim Released { get; } = new();

        public void Release() => Released.Set();

        /// <summary>Releases a still-waiting thread first (e.g. when the test failed before releasing), then frees the event.</summary>
        public void Dispose()
        {
            Released.Set();
            Released.Dispose();
        }
    }
}
