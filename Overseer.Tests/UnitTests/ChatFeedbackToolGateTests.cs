namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using Overseer.Services.Feedback;
using Xunit;

/// <summary>
/// <see cref="ChatFeedbackToolGate"/>: the production chat offers <c>report_feedback</c> only when the
/// administrator's switch and the user's setting are both on, in a session that is neither ephemeral
/// nor confidential.
/// </summary>
public sealed class ChatFeedbackToolGateTests
{
    public static IEnumerable<TheoryDataRow<bool, bool, bool, bool>> AllCombinations()
    {
        foreach (bool admin in new[] { false, true })
        foreach (bool user in new[] { false, true })
        foreach (bool ephemeral in new[] { false, true })
        foreach (bool confidential in new[] { false, true })
        {
            yield return new(admin, user, ephemeral, confidential);
        }
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void IsOffered_OnlyWhenAdminAndUserAllowItInAnOrdinarySession(bool admin, bool user, bool ephemeral, bool confidential)
    {
        bool expected = (admin, user, ephemeral, confidential) == (true, true, false, false);

        Assert.Equal(expected, ChatFeedbackToolGate.IsOffered(admin, user, ephemeral, confidential));
    }
}
