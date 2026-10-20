namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Overseer.Services;
using Overseer.Tests.Helpers;
using Xunit;

public class CancellationExceptionExtensionsTests
{
    public static IEnumerable<object[]> CancellationShapes()
    {
        yield return new object[] { new OperationCanceledException() };
        yield return new object[] { new TaskCanceledException() };
        yield return new object[] { new CanceledQueryException() };
        yield return new object[] { new DbUpdateException("An error occurred while saving the entity changes.", new CanceledQueryException()) };
        yield return new object[] { new InvalidOperationException("An exception has been raised that is likely due to a transient failure.", new CanceledQueryException()) };
        yield return new object[] { new IOException("The client reset the request stream.") };
        yield return new object[] { new AggregateException(new InvalidOperationException("unrelated"), new CanceledQueryException()) };
    }

    [Theory]
    [MemberData(nameof(CancellationShapes))]
    public void IsCanceledBy_IsTrue_ForACancellationShape_WhenTheTokenIsCanceled(Exception ex)
    {
        Assert.True(ex.IsCanceledBy(new CancellationToken(canceled: true)));
    }

    [Theory]
    [MemberData(nameof(CancellationShapes))]
    public void IsCanceledBy_IsFalse_ForACancellationShape_WhenTheTokenIsNotCanceled(Exception ex)
    {
        Assert.False(ex.IsCanceledBy(CancellationToken.None));
    }

    [Fact]
    public void IsCanceledBy_IsFalse_ForAnExceptionWithNoCancellationShapeInItsChain()
    {
        var canceled = new CancellationToken(canceled: true);

        Assert.False(new InvalidOperationException("A second operation was started on this context instance.").IsCanceledBy(canceled));
        Assert.False(new InvalidOperationException("outer", new ArgumentException("inner")).IsCanceledBy(canceled));
        Assert.False(new HttpRequestException("Connection refused.").IsCanceledBy(canceled));
    }

    [Fact]
    public void IsCanceledBy_Terminates_OnASelfReferencingChain()
    {
        var ex = new InvalidOperationException("loop");
        typeof(Exception).GetField("_innerException", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(ex, ex);
        Assert.Same(ex, ex.InnerException);

        Assert.False(ex.IsCanceledBy(new CancellationToken(canceled: true)));
    }

    [Fact]
    public void IsCanceledBy_IsFalse_WhenTheCancellationShapeIsDeeperThanTheChainLimit()
    {
        Exception ex = new CanceledQueryException();
        for (int i = 0; i < 6; i++)
        {
            ex = new InvalidOperationException("wrapper " + i, ex);
        }

        Assert.False(ex.IsCanceledBy(new CancellationToken(canceled: true)));
    }
}
