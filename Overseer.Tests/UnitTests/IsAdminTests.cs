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

        [Fact]
        public void NullUserName_ReturnsFalse()
        {
            var config = CreateConfig("Admin1");
            Assert.False(config.IsAdmin(null));
        }

        [Fact]
        public void EmptyUserName_ReturnsFalse()
        {
            var config = CreateConfig("Admin1");
            Assert.False(config.IsAdmin(""));
        }

        [Fact]
        public void UserNotInList_ReturnsFalse()
        {
            var config = CreateConfig("Admin1,Admin2");
            Assert.False(config.IsAdmin("Intruder"));
        }

        [Fact]
        public void UserInList_ReturnsTrue()
        {
            var config = CreateConfig("Admin1,Admin2");
            Assert.True(config.IsAdmin("Admin1"));
        }

        [Fact]
        public void CaseInsensitive_ReturnsTrue()
        {
            var config = CreateConfig("Admin1");
            Assert.True(config.IsAdmin("admin1"));
        }

        [Fact]
        public void WhitespaceInConfig_ReturnsTrue()
        {
            var config = CreateConfig(" Admin1 , Admin2 ");
            Assert.True(config.IsAdmin("Admin2"));
        }

        [Fact]
        public void EmptyConfig_ReturnsFalse()
        {
            var config = CreateConfig("");
            Assert.False(config.IsAdmin("Admin1"));
        }

        [Fact]
        public void MissingConfigKey_ReturnsFalse()
        {
            var config = CreateConfig(null);
            Assert.False(config.IsAdmin("Admin1"));
        }
    }
}
