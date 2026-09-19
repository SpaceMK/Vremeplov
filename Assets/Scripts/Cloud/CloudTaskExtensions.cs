using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Helpers for the fire-and-forget calls the cloud layer makes.
///
/// Named to avoid colliding with <c>System.Threading.Tasks.TaskExtensions</c>, which
/// is in scope in most of these files.
/// </summary>
public static class CloudTaskExtensions
{
    /// <summary>
    /// Start a task and stop caring about the result — but log a failure rather than
    /// let it vanish. An unawaited Task swallows its exception entirely, which is how
    /// a silently broken cloud sync goes unnoticed for weeks.
    /// </summary>
    public static async void Forget(this Task task)
    {
        if (task == null) return;
        try { await task; }
        catch (Exception e) { Debug.LogWarning($"[Cloud] Background task failed: {e.Message}"); }
    }
}
