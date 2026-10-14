using System;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services;

namespace Overseer.Tests.Helpers;

/// <summary>
/// An empty knowledge base (no <c>KbPath</c>) shared by the tests that only need one to construct a runner or tool.
/// </summary>
internal static class TestKnowledgeBase
{
    private static readonly Lazy<KnowledgeBaseService> Instance = new(
        () => new KnowledgeBaseService(NullLogger<KnowledgeBaseService>.Instance, new ConfigurationBuilder().Build()),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The shared instance. It is never disposed, so the test process holds one reload timer. Register it as an
    /// instance (<c>AddSingleton(TestKnowledgeBase.Empty)</c>), never through a factory, which a disposed provider
    /// would dispose.
    /// </summary>
    public static KnowledgeBaseService Empty => Instance.Value;
}
