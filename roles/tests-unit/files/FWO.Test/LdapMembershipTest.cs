using FWO.Data;
using FWO.Data.Middleware;
using FWO.Middleware.Server;
using Novell.Directory.Ldap;
using NUnit.Framework;
using System.Reflection;

namespace FWO.Test
{
    [TestFixture]
    public class LdapMembershipTest
    {
        [Test]
        public void GetGroupsIncludesWritePathMemberships()
        {
            Ldap ldap = new()
            {
                GroupSearchPath = "ou=search,dc=example,dc=com",
                GroupWritePath = "ou=write,dc=example,dc=com"
            };

            LdapAttributeSet attrs = new();
            attrs.Add(new LdapAttribute("memberOf", new[]
            {
                "cn=AppOwners,ou=write,dc=example,dc=com"
            }));
            LdapEntry user = new("cn=test,dc=example,dc=com", attrs);

            var groups = ldap.GetGroups(user);

            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0], Is.EqualTo("cn=AppOwners,ou=write,dc=example,dc=com"));
        }

        [Test]
        public void GetGroupsIncludesSearchPathMemberships()
        {
            Ldap ldap = new()
            {
                GroupSearchPath = "ou=search,dc=example,dc=com",
                GroupWritePath = "ou=write,dc=example,dc=com"
            };

            LdapAttributeSet attrs = new();
            attrs.Add(new LdapAttribute("memberOf", new[]
            {
                "cn=AppOwners,ou=search,dc=example,dc=com"
            }));
            LdapEntry user = new("cn=test,dc=example,dc=com", attrs);

            var groups = ldap.GetGroups(user);

            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0], Is.EqualTo("cn=AppOwners,ou=search,dc=example,dc=com"));
        }

        [Test]
        public void GetGroupsIgnoresUnrelatedMemberships()
        {
            Ldap ldap = new()
            {
                GroupSearchPath = "ou=search,dc=example,dc=com",
                GroupWritePath = "ou=write,dc=example,dc=com"
            };

            LdapAttributeSet attrs = new();
            attrs.Add(new LdapAttribute("memberOf", new[]
            {
                "cn=OtherGroup,ou=other,dc=example,dc=com"
            }));
            LdapEntry user = new("cn=test,dc=example,dc=com", attrs);

            var groups = ldap.GetGroups(user);

            Assert.That(groups, Is.Empty);
        }

        [Test]
        public void HasGroupHandlingUsesWritePathFallback()
        {
            Ldap ldap = new()
            {
                GroupSearchPath = "",
                GroupWritePath = "ou=write,dc=example,dc=com"
            };

            Assert.That(ldap.HasGroupHandling(), Is.True);
        }

        [Test]
        public void BuildGroupDnsCreatesDnsFromNames()
        {
            var dns = Ldap.BuildGroupDns(new[] { "AppOwners", "SecTeam" }, "ou=groups,dc=example,dc=com");

            Assert.That(dns, Has.Count.EqualTo(2));
            Assert.That(dns, Does.Contain("cn=AppOwners,ou=groups,dc=example,dc=com"));
            Assert.That(dns, Does.Contain("cn=SecTeam,ou=groups,dc=example,dc=com"));
        }

        [Test]
        public void BuildGroupDnsKeepsExistingDnsAndDeduplicates()
        {
            var dns = Ldap.BuildGroupDns(new[]
            {
                "AppOwners",
                "cn=AppOwners,ou=groups,dc=example,dc=com",
                "APPOWNERS"
            }, "ou=groups,dc=example,dc=com");

            Assert.That(dns, Has.Count.EqualTo(1));
            Assert.That(dns, Does.Contain("cn=AppOwners,ou=groups,dc=example,dc=com"));
        }

        [Test]
        public void BuildGroupDnsReturnsEmptyWhenPathMissing()
        {
            var dns = Ldap.BuildGroupDns(new[] { "AppOwners" }, "");

            Assert.That(dns, Is.Empty);
        }

        [Test]
        public async Task ResolveUsersFromDns_ReturnsDistinctDirectUsersWhenSearchPathsAreMissing()
        {
            Ldap ldap = new()
            {
                UserSearchPath = "",
                GroupSearchPath = ""
            };

            List<string> resolved = await ldap.ResolveUsersFromDns([
                "uid=user,ou=users,dc=example,dc=com",
                "UID=USER,ou=users,dc=example,dc=com",
                "",
                "cn=group,ou=groups,dc=example,dc=com"
            ]);

            Assert.That(resolved, Is.EquivalentTo(new[]
            {
                "uid=user,ou=users,dc=example,dc=com",
                "cn=group,ou=groups,dc=example,dc=com"
            }));
            Assert.That(resolved, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task ResolveUsersFromDns_ReturnsEmptyForNullInput()
        {
            Ldap ldap = new();

            List<string> resolved = await ldap.ResolveUsersFromDns(null!);

            Assert.That(resolved, Is.Empty);
        }

        [Test]
        public void GetMemberKey_ReturnsMemberForActiveDirectoryAndUniqueMemberOtherwise()
        {
            Ldap ldap = new()
            {
                Type = (int)LdapType.ActiveDirectory
            };

            Assert.That(InvokePrivate<string>(ldap, "GetMemberKey"), Is.EqualTo("member"));

            ldap.Type = (int)LdapType.Default;

            Assert.That(InvokePrivate<string>(ldap, "GetMemberKey"), Is.EqualTo("uniqueMember"));
        }

        [Test]
        public void IsGroupDnAndIsUserDn_UseConfiguredSearchPaths()
        {
            Ldap ldap = new()
            {
                GroupSearchPath = "ou=groups,dc=example,dc=com",
                UserSearchPath = "ou=users,dc=example,dc=com"
            };

            Assert.Multiple(() =>
            {
                Assert.That(InvokePrivate<bool>(ldap, "IsGroupDn", "cn=team,ou=groups,dc=example,dc=com"), Is.True);
                Assert.That(InvokePrivate<bool>(ldap, "IsGroupDn", "uid=user,ou=users,dc=example,dc=com"), Is.False);
                Assert.That(InvokePrivate<bool>(ldap, "IsUserDn", "uid=user,ou=users,dc=example,dc=com"), Is.True);
                Assert.That(InvokePrivate<bool>(ldap, "IsUserDn", "cn=team,ou=groups,dc=example,dc=com"), Is.False);
            });
        }

        [Test]
        public async Task GetAllRoles_ReturnsEmptyWhenRoleHandlingIsDisabled()
        {
            Ldap ldap = new()
            {
                RoleSearchPath = ""
            };

            List<RoleGetReturnParameters> roles = await ldap.GetAllRoles();

            Assert.That(roles, Is.Empty);
        }

        [Test]
        public async Task RemoveUserFromAllEntries_ReturnsTrueWhenNoMembershipHandlingIsConfigured()
        {
            Ldap ldap = new()
            {
                RoleSearchPath = "",
                GroupSearchPath = "",
                GroupWritePath = ""
            };

            bool removed = await ldap.RemoveUserFromAllEntries("uid=user,ou=users,dc=example,dc=com");

            Assert.That(removed, Is.True);
        }

        private static T InvokePrivate<T>(object instance, string methodName, params object?[] parameters)
        {
            MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingMethodException(instance.GetType().FullName, methodName);
            return (T)method.Invoke(instance, parameters)!;
        }
    }
}
