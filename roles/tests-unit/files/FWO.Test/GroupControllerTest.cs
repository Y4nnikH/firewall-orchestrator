using FWO.Data;
using FWO.Data.Middleware;
using FWO.Middleware.Server.Controllers;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using MiddlewareLdap = FWO.Middleware.Server.Ldap;

namespace FWO.Test
{
    [TestFixture]
    internal class GroupControllerTest
    {
        [Test]
        public async Task Get_ReturnsEmptyListWhenNoLdapsAreConfigured()
        {
            GroupController controller = new(new List<MiddlewareLdap>());

            ActionResult<List<GroupGetReturnParameters>> result = await controller.Get();

            Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
            Assert.That(((OkObjectResult)result.Result!).Value as List<GroupGetReturnParameters>, Is.Empty);
        }

        [Test]
        public async Task GetMembers_ReturnsEmptyListWhenGroupDnIsMissing()
        {
            GroupController controller = new(new List<MiddlewareLdap>());

            List<string> result = await controller.GetMembers(new GroupMemberGetParameters { GroupDn = "" });

            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task GetMemberships_ReturnsEmptyListWhenNoUserIsProvided()
        {
            GroupController controller = new(new List<MiddlewareLdap>());

            List<string> result = await controller.GetMemberships(new GroupMembershipGetParameters());

            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task ResolveMembers_ReturnsEmptyListWhenDnsAreMissing()
        {
            GroupController controller = new(new List<MiddlewareLdap>());

            List<string> result = await controller.ResolveMembers(new GroupResolveParameters { Dns = [] });

            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task AddUser_ReturnsFalseWhenNoWritableLdapMatches()
        {
            GroupController controller = new(new List<MiddlewareLdap>());

            bool result = await controller.AddUser(new GroupAddDeleteUserParameters
            {
                UserDn = "uid=user,dc=example,dc=com",
                GroupDn = "cn=group,dc=example,dc=com"
            });

            Assert.That(result, Is.False);
        }

        [Test]
        public async Task RemoveUser_ReturnsFalseWhenNoWritableLdapMatches()
        {
            GroupController controller = new(new List<MiddlewareLdap>());

            bool result = await controller.RemoveUser(new GroupAddDeleteUserParameters
            {
                UserDn = "uid=user,dc=example,dc=com",
                GroupDn = "cn=group,dc=example,dc=com"
            });

            Assert.That(result, Is.False);
        }
    }
}
