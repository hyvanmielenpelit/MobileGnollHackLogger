using Microsoft.Extensions.Configuration;
using Overseer.Extensions;
using System.Collections.Generic;
using Xunit;

namespace Overseer.Tests.UnitTests
{
    public class IsAdminTests
    {
        private IConfiguration CreateConfig(string? adminsValue)
        {
            var dict = new Dictionary<string, string?>();
            if (adminsValue != null)
            {
                dict.Add("Admins", adminsValue);
            }
            return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        }

        [Theory]
        [InlineData("Admin1", null, false)]                     // Null user name
        [InlineData("Admin1", "", false)]                       // Empty user name
        [InlineData("Admin1,Admin2", "Intruder", false)]        // User not in the list
        [InlineData("Admin1,Admin2", "Admin1", true)]           // User in the list
        [InlineData("Admin1", "admin1", true)]                  // Case-insensitive match
        [InlineData(" Admin1 , Admin2 ", "Admin2", true)]       // Whitespace around listed names
        [InlineData("", "Admin1", false)]                       // Empty Admins value
        [InlineData(null, "Admin1", false)]                     // Missing Admins key
        public void IsAdmin_IsTrueOnlyForAListedUserName(string? adminsValue, string? userName, bool expected)
        {
            var config = CreateConfig(adminsValue);
            Assert.Equal(expected, config.IsAdmin(userName));
        }
    }
}
