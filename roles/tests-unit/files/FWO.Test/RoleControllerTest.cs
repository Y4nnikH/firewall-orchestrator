using FWO.Data;
using FWO.Data.Middleware;
using FWO.Middleware.Server.Controllers;
using NUnit.Framework;
using MiddlewareLdap = FWO.Middleware.Server.Ldap;

namespace FWO.Test
{
    [TestFixture]
    internal class RoleControllerTest
    {
        [Test]
        public async Task Get_ReturnsEmptyListWhenNoLdapsAreConfigured()
        {
            RoleController controller = new(new List<MiddlewareLdap>());

            List<RoleGetReturnParameters> result = await controller.Get();

            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task AddUser_ReturnsFalseWhenNoWritableLdapMatches()
        {
            RoleController controller = new(new List<MiddlewareLdap>());

            bool result = await controller.AddUser(new RoleAddDeleteUserParameters
            {
                UserDn = "uid=user,dc=example,dc=com",
                Role = "cn=role,dc=example,dc=com"
            });

            Assert.That(result, Is.False);
        }

        [Test]
        public async Task RemoveUser_ReturnsFalseWhenNoWritableLdapMatches()
        {
            RoleController controller = new(new List<MiddlewareLdap>());

            bool result = await controller.RemoveUser(new RoleAddDeleteUserParameters
            {
                UserDn = "uid=user,dc=example,dc=com",
                Role = "cn=role,dc=example,dc=com"
            });

            Assert.That(result, Is.False);
        }
    }
}
