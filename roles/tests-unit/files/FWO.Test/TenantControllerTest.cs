using FWO.Api.Client;
using FWO.Api.Client.Queries;
using FWO.Data;
using FWO.Data.Middleware;
using FWO.Middleware.Server.Controllers;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using MiddlewareLdap = FWO.Middleware.Server.Ldap;

namespace FWO.Test
{
    [TestFixture]
    internal class TenantControllerTest
    {
        [Test]
        public async Task Get_ReturnsConvertedTenants()
        {
            TenantControllerTestApiConnection apiConnection = new()
            {
                Tenants =
                [
                    new Tenant
                    {
                        Id = 7,
                        Name = "tenant-1",
                        Comment = "comment",
                        Project = "project",
                        ViewAllDevices = true
                    }
                ]
            };
            TenantController controller = new(new List<MiddlewareLdap>(), apiConnection);

            List<TenantGetReturnParameters> result = await controller.Get();

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Id, Is.EqualTo(7));
            Assert.That(result[0].Name, Is.EqualTo("tenant-1"));
            Assert.That(result[0].Comment, Is.EqualTo("comment"));
            Assert.That(result[0].Project, Is.EqualTo("project"));
            Assert.That(result[0].ViewAllDevices, Is.True);
            Assert.That(apiConnection.LastQuery, Is.EqualTo(AuthQueries.getTenants));
            Assert.That(apiConnection.QueryCount, Is.EqualTo(1));
        }

        [Test]
        public async Task Post_ReturnsZeroWithoutWritableLdap()
        {
            TenantControllerTestApiConnection apiConnection = new();
            TenantController controller = new(new List<MiddlewareLdap>(), apiConnection);

            int result = await controller.Post(new TenantAddParameters
            {
                Name = "tenant-1",
                ViewAllDevices = true
            });

            Assert.That(result, Is.Zero);
            Assert.That(apiConnection.QueryCount, Is.Zero);
        }

        [Test]
        public async Task Change_ReturnsTrueWhenDatabaseUpdateMatchesTenantId()
        {
            TenantControllerTestApiConnection apiConnection = new()
            {
                UpdateResult = new ReturnId { UpdatedId = 7 }
            };
            TenantController controller = new(new List<MiddlewareLdap>(), apiConnection);

            bool result = await controller.Change(new TenantEditParameters
            {
                Id = 7,
                Comment = "updated",
                Project = "project",
                ViewAllDevices = true
            });

            Assert.That(result, Is.True);
            Assert.That(apiConnection.LastQuery, Is.EqualTo(AuthQueries.updateTenant));
            Assert.That(apiConnection.QueryCount, Is.EqualTo(1));
        }

        [Test]
        public async Task Delete_ReturnsTrueWhenDatabaseDeleteMatchesTenantId()
        {
            TenantControllerTestApiConnection apiConnection = new()
            {
                DeleteResult = new ReturnId { DeletedId = 7 }
            };
            TenantController controller = new(new List<MiddlewareLdap>(), apiConnection);

            bool result = await controller.Delete(new TenantDeleteParameters
            {
                Id = 7,
                Name = "tenant-1"
            });

            Assert.That(result, Is.True);
            Assert.That(apiConnection.LastQuery, Is.EqualTo(AuthQueries.deleteTenant));
            Assert.That(apiConnection.QueryCount, Is.EqualTo(1));
        }

        private sealed class TenantControllerTestApiConnection : SimulatedApiConnection
        {
            public Tenant[] Tenants { get; set; } = [];
            public ReturnId UpdateResult { get; set; } = new();
            public ReturnId DeleteResult { get; set; } = new();
            public string? LastQuery { get; private set; }
            public object? LastVariables { get; private set; }
            public int QueryCount { get; private set; }

            public override Task<QueryResponseType> SendQueryAsync<QueryResponseType>(string query, object? variables = null, string? operationName = null, QueryChunkingOptions? chunkingOptions = null)
            {
                LastQuery = query;
                LastVariables = variables;
                QueryCount++;

                if (typeof(QueryResponseType) == typeof(Tenant[]) && query == AuthQueries.getTenants)
                {
                    return Task.FromResult((QueryResponseType)(object)Tenants);
                }

                if (typeof(QueryResponseType) == typeof(ReturnId) && query == AuthQueries.updateTenant)
                {
                    return Task.FromResult((QueryResponseType)(object)UpdateResult);
                }

                if (typeof(QueryResponseType) == typeof(ReturnId) && query == AuthQueries.deleteTenant)
                {
                    return Task.FromResult((QueryResponseType)(object)DeleteResult);
                }

                if (typeof(QueryResponseType) == typeof(ReturnIdWrapper) && query == AuthQueries.addTenant)
                {
                    return Task.FromResult((QueryResponseType)(object)new ReturnIdWrapper());
                }

                throw new AssertionException($"Unexpected query: {query} for type {typeof(QueryResponseType).Name}");
            }
        }
    }
}
