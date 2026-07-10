using Bunit;
using FWO.Api.Client;
using FWO.Api.Client.Queries;
using FWO.Basics;
using FWO.Config.Api;
using FWO.Data;
using FWO.Data.Flow;
using FWO.Ui.Pages.Settings;
using FWO.Ui.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Security.Claims;
using System.Linq;
using System.Reflection;

namespace FWO.Test
{
    [TestFixture]
    internal class UiFlowSettingsPagesTest
    {
        private static readonly string[] kFilteredCandidateTypes = ["host", "host"];

        [SetUp]
        public void SetUp()
        {
            SeedTranslations();
        }

        [Test]
        public async Task FlowNetworkGroupsPage_RendersWithoutErrors()
        {
            await using BunitContext context = CreateContext();

            IRenderedComponent<SettingsFlowNetworkGroups> component = RenderPage<SettingsFlowNetworkGroups>(context);

            component.WaitForAssertion(() => Assert.That(component.Markup, Does.Contain("Flow Network Group")));
        }

        [Test]
        public async Task FlowNetworkGroupsPage_ShowsMemberCountFromFlowMembers()
        {
            await using BunitContext context = CreateContext();

            IRenderedComponent<SettingsFlowNetworkGroups> component = RenderPage<SettingsFlowNetworkGroups>(context);

            component.WaitForAssertion(() =>
            {
                var tables = component.FindAll("table");
                var objectsCell = tables[tables.Count - 1]
                    .QuerySelectorAll("tbody tr")[0]
                    .Children[5];

                Assert.That(objectsCell.TextContent.Trim(), Is.EqualTo("2"));
            });
        }

        [Test]
        public async Task FlowServiceObjectsPage_RendersWithoutErrors()
        {
            await using BunitContext context = CreateContext();

            IRenderedComponent<SettingsFlowServiceObjects> component = RenderPage<SettingsFlowServiceObjects>(context);

            component.WaitForAssertion(() => Assert.That(component.Markup, Does.Contain("Flow Service Object")));
            component.WaitForAssertion(() => Assert.That(component.Markup, Does.Contain("TCP")));
        }

        [Test]
        public async Task FlowServiceObjectsPage_CreateCustomObject_SendsInsertAndMapping()
        {
            await using BunitContext context = CreateCustomServiceCreateContext(out FlowServiceObjectsCustomCreateApiConn apiConnection);

            IRenderedComponent<SettingsFlowServiceObjects> component = RenderPage<SettingsFlowServiceObjects>(context);
            component.WaitForAssertion(() => Assert.That(component.FindAll("button.btn.btn-sm.btn-primary"), Is.Not.Empty));

            component.FindAll("button.btn.btn-sm.btn-primary")[0].Click();
            component.WaitForAssertion(() => Assert.That(component.FindAll("input.form-control.form-control-sm"), Is.Not.Empty));

            component.FindAll("input.form-control.form-control-sm")[0].Change("Custom Service");
            component.FindAll("button.btn-outline-primary")[0].Click();
            component.WaitForAssertion(() => Assert.That(component.FindAll("button.btn-success"), Is.Not.Empty));
            component.FindAll("button.btn.btn-sm.btn-primary")[^1].Click();

            component.WaitForAssertion(() =>
            {
                Assert.That(apiConnection.Queries, Does.Contain(FlowQueries.insertFlowSvcObjects));
                Assert.That(apiConnection.Queries, Does.Contain(FlowMutations.upsertFlowSvcObjectMapping));
                Assert.That(apiConnection.InsertedServiceObject, Is.Not.Null);
                Assert.That(apiConnection.InsertedServiceObject!.Name, Is.EqualTo("Custom Service"));
                Assert.That(apiConnection.InsertedServiceObject.PortStart, Is.EqualTo(80));
                Assert.That(apiConnection.InsertedServiceObject.PortEnd, Is.EqualTo(80));
                Assert.That(apiConnection.InsertedServiceObject.IpProtoId, Is.EqualTo(6));
                Assert.That(apiConnection.InsertedServiceObject.SvcObjHash, Is.EqualTo(FlowHashGenerator.GenerateSvcObjectHash(6, 80, 80)));
                Assert.That(apiConnection.MappingCalls, Is.EqualTo(new List<(long ServiceId, long FlowSvcobjId, bool ActiveOnMgm)>
                {
                    (11, 900, true)
                }));
            });
        }

        [Test]
        public async Task FlowServiceObjectsPage_CreateCustomObject_DoesNotOfferServiceGroupCandidates()
        {
            await using BunitContext context = CreateCustomServiceCreateContext(out _);

            IRenderedComponent<SettingsFlowServiceObjects> component = RenderPage<SettingsFlowServiceObjects>(context);
            component.WaitForAssertion(() => Assert.That(component.FindAll("button.btn.btn-sm.btn-primary"), Is.Not.Empty));

            component.FindAll("button.btn.btn-sm.btn-primary")[0].Click();

            component.WaitForAssertion(() =>
            {
                Assert.That(component.Markup, Does.Not.Contain("Service Group Candidate"));
                Assert.That(component.Markup, Does.Contain("Service A"));
            });
        }

        [Test]
        public async Task FlowServiceObjectsPage_CreateCustomObject_RejectsMixedTechnicalDefinitionSelection()
        {
            await using BunitContext context = CreateCustomServiceCreateContext(out FlowServiceObjectsCustomCreateApiConn apiConnection);

            IRenderedComponent<SettingsFlowServiceObjects> component = RenderPage<SettingsFlowServiceObjects>(context);
            string? errorMessage = null;
            SetMember(component.Instance, "DisplayMessageInUi", new Action<Exception?, string, string, bool>((exception, _, message, _) =>
            {
                errorMessage = exception?.Message ?? message;
            }));
            component.WaitForAssertion(() => Assert.That(component.FindAll("button.btn.btn-sm.btn-primary"), Is.Not.Empty));

            component.FindAll("button.btn.btn-sm.btn-primary")[0].Click();
            component.WaitForAssertion(() => Assert.That(component.FindAll("input.form-control.form-control-sm"), Is.Not.Empty));

            component.FindAll("input.form-control.form-control-sm")[0].Change("Custom Service");
            component.FindAll("button.btn-outline-primary")[0].Click();
            component.FindAll("button.btn-outline-primary")[^1].Click();
            component.FindAll("button.btn.btn-sm.btn-primary")[^1].Click();

            component.WaitForAssertion(() =>
            {
                Assert.That(errorMessage, Is.EqualTo("Selected services must share the same protocol and port range"));
                Assert.That(apiConnection.InsertedServiceObject, Is.Null);
                Assert.That(apiConnection.MappingCalls, Is.Empty);
                Assert.That(apiConnection.Queries, Does.Not.Contain(FlowQueries.insertFlowSvcObjects));
                Assert.That(apiConnection.Queries, Does.Not.Contain(FlowMutations.upsertFlowSvcObjectMapping));
            });
        }

        [Test]
        public async Task FlowServiceObjectsPage_CreateCustomObject_ShowsNameMissingError()
        {
            await using BunitContext context = CreateCustomServiceCreateContext(out FlowServiceObjectsCustomCreateApiConn apiConnection);

            IRenderedComponent<SettingsFlowServiceObjects> component = RenderPage<SettingsFlowServiceObjects>(context);
            string? errorMessage = null;
            SetMember(component.Instance, "DisplayMessageInUi", new Action<Exception?, string, string, bool>((exception, _, message, _) =>
            {
                errorMessage = exception?.Message ?? message;
            }));
            component.WaitForAssertion(() => Assert.That(component.FindAll("button.btn.btn-sm.btn-primary"), Is.Not.Empty));

            component.FindAll("button.btn.btn-sm.btn-primary")[0].Click();
            component.WaitForAssertion(() => Assert.That(component.FindAll("input.form-control.form-control-sm"), Is.Not.Empty));

            component.FindAll("button.btn.btn-sm.btn-primary")[^1].Click();

            component.WaitForAssertion(() =>
            {
                Assert.That(errorMessage, Is.EqualTo("Please enter a name for the custom flow object"));
                Assert.That(apiConnection.Queries, Does.Not.Contain(FlowQueries.insertFlowSvcObjects));
                Assert.That(apiConnection.Queries, Does.Not.Contain(FlowMutations.upsertFlowSvcObjectMapping));
            });
        }

        [Test]
        public async Task FlowServiceObjectsPage_CreateCustomObject_ShowsNoServiceSelectedError()
        {
            await using BunitContext context = CreateCustomServiceCreateContext(out FlowServiceObjectsCustomCreateApiConn apiConnection);

            IRenderedComponent<SettingsFlowServiceObjects> component = RenderPage<SettingsFlowServiceObjects>(context);
            string? errorMessage = null;
            SetMember(component.Instance, "DisplayMessageInUi", new Action<Exception?, string, string, bool>((exception, _, message, _) =>
            {
                errorMessage = exception?.Message ?? message;
            }));
            component.WaitForAssertion(() => Assert.That(component.FindAll("button.btn.btn-sm.btn-primary"), Is.Not.Empty));

            component.FindAll("button.btn.btn-sm.btn-primary")[0].Click();
            component.WaitForAssertion(() => Assert.That(component.FindAll("input.form-control.form-control-sm"), Is.Not.Empty));

            component.FindAll("input.form-control.form-control-sm")[0].Change("Custom Service");
            component.FindAll("button.btn.btn-sm.btn-primary")[^1].Click();

            component.WaitForAssertion(() =>
            {
                Assert.That(errorMessage, Is.EqualTo("Please select at least one service"));
                Assert.That(apiConnection.Queries, Does.Not.Contain(FlowQueries.insertFlowSvcObjects));
                Assert.That(apiConnection.Queries, Does.Not.Contain(FlowMutations.upsertFlowSvcObjectMapping));
            });
        }

        [Test]
        public async Task FlowServiceObjectsPage_CreateCustomObject_AllowsProtocolOnlyService()
        {
            await using BunitContext context = CreateProtocolOnlyServiceCreateContext(out FlowServiceObjectsProtocolOnlyApiConn apiConnection);

            IRenderedComponent<SettingsFlowServiceObjects> component = RenderPage<SettingsFlowServiceObjects>(context);
            string? errorMessage = null;
            SetMember(component.Instance, "DisplayMessageInUi", new Action<Exception?, string, string, bool>((exception, _, message, _) =>
            {
                errorMessage = exception?.Message ?? message;
            }));
            component.WaitForAssertion(() => Assert.That(component.FindAll("button.btn.btn-sm.btn-primary"), Is.Not.Empty));

            component.FindAll("button.btn.btn-sm.btn-primary")[0].Click();
            component.WaitForAssertion(() => Assert.That(component.FindAll("input.form-control.form-control-sm"), Is.Not.Empty));

            component.FindAll("input.form-control.form-control-sm")[0].Change("Protocol Only");
            component.FindAll("button.btn-outline-primary")[0].Click();
            component.FindAll("button.btn.btn-sm.btn-primary")[^1].Click();

            component.WaitForAssertion(() =>
            {
                Assert.That(apiConnection.Queries, Does.Contain(FlowQueries.insertFlowSvcObjects));
                Assert.That(apiConnection.Queries, Does.Contain(FlowMutations.upsertFlowSvcObjectMapping));
                Assert.That(apiConnection.InsertedServiceObject, Is.Not.Null);
                Assert.That(apiConnection.InsertedServiceObject!.Name, Is.EqualTo("Protocol Only"));
                Assert.That(apiConnection.InsertedServiceObject.PortStart, Is.Null);
                Assert.That(apiConnection.InsertedServiceObject.PortEnd, Is.Null);
                Assert.That(apiConnection.InsertedServiceObject.IpProtoId, Is.EqualTo(1));
                Assert.That(apiConnection.InsertedServiceObject.SvcObjHash, Is.Not.Null.And.Length.EqualTo(32));
                Assert.That(apiConnection.MappingCalls, Is.EqualTo(new List<(long ServiceId, long FlowSvcobjId, bool ActiveOnMgm)>
                {
                    (11, 900, true)
                }));
                Assert.That(errorMessage, Is.Null, errorMessage);
            }, TimeSpan.FromSeconds(3));
        }

        [Test]
        public async Task FlowServiceObjectsPage_ResolveDuplicateMapping_SendsExpectedMutations()
        {
            await using BunitContext context = CreateDuplicateResolverContext(out FlowServiceObjectsDuplicateResolverApiConn apiConnection);

            IRenderedComponent<SettingsFlowServiceObjects> component = RenderPage<SettingsFlowServiceObjects>(context);
            component.WaitForAssertion(() => Assert.That(component.FindAll("button.btn.btn-sm.btn-warning"), Is.Not.Empty));

            component.FindAll("button.btn.btn-sm.btn-warning")[0].Click();
            component.WaitForAssertion(() => Assert.That(component.FindAll("button.btn-outline-primary"), Is.Not.Empty));
            component.FindAll("button.btn-outline-primary")[^1].Click();
            component.FindAll("button.btn.btn-sm.btn-warning")[^1].Click();

            component.WaitForAssertion(() =>
            {
                Assert.That(apiConnection.Queries, Does.Contain(FlowMutations.upsertFlowSvcObjectMapping));
                Assert.That(apiConnection.MappingCalls, Is.EqualTo(new List<(long ServiceId, long FlowSvcobjId, bool ActiveOnMgm)>
                {
                    (11, 100, false),
                    (12, 100, true)
                }));
                Assert.That(apiConnection.Queries.Count(query => query == FlowQueries.getFlowServiceObjects), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task FlowServiceObjectsPage_ResolveDuplicateMapping_PreservesCachedServiceSignatureForCreateDialog()
        {
            await using BunitContext context = CreateDuplicateResolverContext(out FlowServiceObjectsDuplicateResolverApiConn apiConnection);

            IRenderedComponent<SettingsFlowServiceObjects> component = RenderPage<SettingsFlowServiceObjects>(context);
            component.WaitForAssertion(() => Assert.That(component.FindAll("button.btn.btn-sm.btn-warning"), Is.Not.Empty));

            component.FindAll("button.btn.btn-sm.btn-warning")[0].Click();
            component.WaitForAssertion(() => Assert.That(component.FindAll("button.btn-outline-primary"), Is.Not.Empty));
            component.FindAll("button.btn-outline-primary")[^1].Click();
            component.FindAll("button.btn.btn-sm.btn-warning")[^1].Click();

            component.WaitForAssertion(() => Assert.That(apiConnection.MappingCalls.Count, Is.EqualTo(2)));

            component.FindAll("button.btn.btn-sm.btn-primary")[0].Click();
            component.WaitForAssertion(() => Assert.That(component.FindAll("input.form-control.form-control-sm"), Is.Not.Empty));

            component.FindAll("input.form-control.form-control-sm")[0].Change("Merged Service");
            component.FindAll("button.btn-outline-primary")[0].Click();
            component.WaitForAssertion(() => Assert.That(component.FindAll("button.btn-success"), Is.Not.Empty));
            component.FindAll("button.btn.btn-sm.btn-primary")[^1].Click();

            component.WaitForAssertion(() =>
            {
                Assert.That(apiConnection.Queries, Does.Contain(FlowQueries.insertFlowSvcObjects));
                Assert.That(apiConnection.InsertedServiceObject, Is.Not.Null);
                Assert.That(apiConnection.InsertedServiceObject!.IpProtoId, Is.EqualTo(6));
                Assert.That(apiConnection.InsertedServiceObject.PortStart, Is.EqualTo(80));
                Assert.That(apiConnection.InsertedServiceObject.PortEnd, Is.EqualTo(80));
                Assert.That(apiConnection.MappingCalls, Does.Contain((11L, 900L, true)));
            });
        }

        [Test]
        public async Task FlowServiceGroupsPage_RendersWithoutErrors()
        {
            await using BunitContext context = CreateContext();

            IRenderedComponent<SettingsFlowServiceGroups> component = RenderPage<SettingsFlowServiceGroups>(context);

            component.WaitForAssertion(() => Assert.That(component.Markup, Does.Contain("Flow Service Group")));
        }

        [Test]
        public async Task FlowTimeObjectsPage_RendersWithoutErrors()
        {
            await using BunitContext context = CreateContext();

            IRenderedComponent<SettingsFlowTimeObjects> component = RenderPage<SettingsFlowTimeObjects>(context);

            component.WaitForAssertion(() => Assert.That(component.Markup, Does.Contain("Flow Time Object")));
        }

        [Test]
        public async Task FlowNetworkObjectsPage_RecalculateNames_UsesNamingManagementCandidates()
        {
            await using BunitContext context = CreateNetworkObjectsContext(out FlowNetworkObjectsNamingApiConn apiConnection);

            IRenderedComponent<SettingsFlowNetworkObjects> component = RenderPage<SettingsFlowNetworkObjects>(context);
            FieldInfo? namingManagementField = typeof(SettingsFlowNetworkObjects).GetField("namingCustomObjectManagements", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(namingManagementField, Is.Not.Null);
            component.WaitForAssertion(() =>
            {
                List<Management> namingManagements = (List<Management>)namingManagementField!.GetValue(component.Instance)!;
                Assert.That(namingManagements, Has.Count.EqualTo(2));
            });

            MethodInfo? saveNamingSource = typeof(SettingsFlowNetworkObjects).GetMethod("SaveNamingSource", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(saveNamingSource, Is.Not.Null);
            await component.InvokeAsync(async () => await (Task)saveNamingSource!.Invoke(component.Instance, null)!);

            component.WaitForAssertion(() =>
            {
                Assert.That(apiConnection.Queries, Does.Contain(FlowQueries.getFlowCustomObjectNamingCandidates));
                Assert.That(apiConnection.UpdatedFlowObjectNames, Has.Count.EqualTo(1).And.Contains("Global Object Name"));
            });
        }

        [Test]
        public async Task FlowNetworkObjectsPage_ShowsSpinnerOnBusySaveButton()
        {
            await using BunitContext context = CreateNetworkObjectsContext(out _);

            IRenderedComponent<SettingsFlowNetworkObjects> component = RenderPage<SettingsFlowNetworkObjects>(context);
            SetMember(component.Instance, "workInProgress", true);
            component.Render();

            component.WaitForAssertion(() =>
            {
                var saveButton = component.FindAll("button.btn.btn-sm.btn-primary")[0];
                Assert.That(saveButton.InnerHtml, Does.Contain("spinner-border"));
                Assert.That(saveButton.GetAttribute("disabled"), Is.Not.Null);
            });
        }

        [Test]
        public async Task FlowNetworkObjectsPage_ResolveDuplicateMapping_PreservesTypeForCustomObjectSearch()
        {
            await using BunitContext context = CreateNetworkDuplicateResolverContext(out FlowNetworkObjectsDuplicateResolverApiConn apiConnection);

            IRenderedComponent<SettingsFlowNetworkObjects> component = RenderPage<SettingsFlowNetworkObjects>(context);
            FieldInfo? duplicateGroupsField = typeof(SettingsFlowNetworkObjects).GetField("duplicateGroups", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo? selectedGroupField = typeof(SettingsFlowNetworkObjects).GetField("SelectedDuplicateGroup", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo? resolveDuplicateMapping = typeof(SettingsFlowNetworkObjects).GetMethod("ResolveDuplicateMapping", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo? openCreateCustomObject = typeof(SettingsFlowNetworkObjects).GetMethod("OpenCreateCustomObject", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(duplicateGroupsField, Is.Not.Null);
            Assert.That(selectedGroupField, Is.Not.Null);
            Assert.That(resolveDuplicateMapping, Is.Not.Null);
            Assert.That(openCreateCustomObject, Is.Not.Null);

            component.WaitForAssertion(() =>
            {
                List<FlowNwObjectDuplicateGroup> duplicateGroups = (List<FlowNwObjectDuplicateGroup>)duplicateGroupsField!.GetValue(component.Instance)!;
                Assert.That(duplicateGroups, Has.Count.EqualTo(1));
            });

            List<FlowNwObjectDuplicateGroup> loadedDuplicateGroups = (List<FlowNwObjectDuplicateGroup>)duplicateGroupsField!.GetValue(component.Instance)!;
            FlowNwObjectDuplicateGroup duplicateGroup = loadedDuplicateGroups.Single();
            selectedGroupField!.SetValue(component.Instance, duplicateGroup);
            await component.InvokeAsync(async () => await (Task)resolveDuplicateMapping!.Invoke(component.Instance, [duplicateGroup.Objects[0]])!);

            component.WaitForAssertion(() => Assert.That(apiConnection.MappingCalls.Count, Is.EqualTo(2)));

            await component.InvokeAsync(async () => await (Task)openCreateCustomObject!.Invoke(component.Instance, null)!);
            FieldInfo? selectionsField = typeof(SettingsFlowNetworkObjects).GetField("customObjectSelections", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(selectionsField, Is.Not.Null);
            System.Collections.IEnumerable selections = (System.Collections.IEnumerable)selectionsField!.GetValue(component.Instance)!;
            object selection = selections.Cast<object>().Single();
            selection.GetType().GetProperty("SearchText")!.SetValue(selection, "host");

            var filteredCandidates = ((System.Collections.IEnumerable)selection.GetType().GetProperty("FilteredCandidates")!.GetValue(selection)!)
                .Cast<NetworkObject>()
                .ToList();

            Assert.That(filteredCandidates, Has.Count.EqualTo(2));
            Assert.That(filteredCandidates.Select(candidate => candidate.Type.Name), Is.EqualTo(kFilteredCandidateTypes));
        }

        private static BunitContext CreateContext()
        {
            BunitContext context = new();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddAuthorizationCore();
            context.Services.AddLocalization();
            context.Services.AddSingleton<IAuthorizationService, AllowAllAuthorizationService>();
            context.Services.AddSingleton<ApiConnection>(new FlowSettingsPagesTestApiConn());
            context.Services.AddScoped<DomEventService>();
            context.Services.AddSingleton<UserConfig>(new SimulatedUserConfig
            {
                User = { Roles = [Roles.Admin] }
            });
            context.Services.AddSingleton<AuthenticationStateProvider>(new FlowSettingsPagesAuthStateProvider(Roles.Admin));
            return context;
        }

        private static BunitContext CreateCustomServiceCreateContext(out FlowServiceObjectsCustomCreateApiConn apiConnection)
        {
            BunitContext context = new();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddAuthorizationCore();
            context.Services.AddLocalization();
            context.Services.AddSingleton<IAuthorizationService, AllowAllAuthorizationService>();
            apiConnection = new FlowServiceObjectsCustomCreateApiConn();
            context.Services.AddSingleton<ApiConnection>(apiConnection);
            context.Services.AddScoped<DomEventService>();
            context.Services.AddSingleton<UserConfig>(new SimulatedUserConfig
            {
                User = { Roles = [Roles.Admin] }
            });
            context.Services.AddSingleton<AuthenticationStateProvider>(new FlowSettingsPagesAuthStateProvider(Roles.Admin));
            return context;
        }

        private static BunitContext CreateProtocolOnlyServiceCreateContext(out FlowServiceObjectsProtocolOnlyApiConn apiConnection)
        {
            BunitContext context = new();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddAuthorizationCore();
            context.Services.AddLocalization();
            context.Services.AddSingleton<IAuthorizationService, AllowAllAuthorizationService>();
            apiConnection = new FlowServiceObjectsProtocolOnlyApiConn();
            context.Services.AddSingleton<ApiConnection>(apiConnection);
            context.Services.AddScoped<DomEventService>();
            context.Services.AddSingleton<UserConfig>(new SimulatedUserConfig
            {
                User = { Roles = [Roles.Admin] }
            });
            context.Services.AddSingleton<AuthenticationStateProvider>(new FlowSettingsPagesAuthStateProvider(Roles.Admin));
            return context;
        }

        private static BunitContext CreateDuplicateResolverContext(out FlowServiceObjectsDuplicateResolverApiConn apiConnection)
        {
            BunitContext context = new();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddAuthorizationCore();
            context.Services.AddLocalization();
            context.Services.AddSingleton<IAuthorizationService, AllowAllAuthorizationService>();
            apiConnection = new FlowServiceObjectsDuplicateResolverApiConn();
            context.Services.AddSingleton<ApiConnection>(apiConnection);
            context.Services.AddScoped<DomEventService>();
            context.Services.AddSingleton<UserConfig>(new SimulatedUserConfig
            {
                User = { Roles = [Roles.Admin] }
            });
            context.Services.AddSingleton<AuthenticationStateProvider>(new FlowSettingsPagesAuthStateProvider(Roles.Admin));
            return context;
        }

        private static BunitContext CreateNetworkDuplicateResolverContext(out FlowNetworkObjectsDuplicateResolverApiConn apiConnection)
        {
            BunitContext context = new();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddAuthorizationCore();
            context.Services.AddLocalization();
            context.Services.AddSingleton<IAuthorizationService>(new AllowAllAuthorizationService());
            apiConnection = new FlowNetworkObjectsDuplicateResolverApiConn();
            context.Services.AddSingleton<ApiConnection>(apiConnection);
            context.Services.AddScoped<DomEventService>();
            context.Services.AddSingleton<GlobalConfig>(new SimulatedGlobalConfig());
            context.Services.AddSingleton<UserConfig>(new SimulatedUserConfig
            {
                User = { Roles = [Roles.Admin] }
            });
            context.Services.AddSingleton<AuthenticationStateProvider>(new FlowSettingsPagesAuthStateProvider(Roles.Admin));
            return context;
        }

        private static BunitContext CreateNetworkObjectsContext(out FlowNetworkObjectsNamingApiConn apiConnection)
        {
            BunitContext context = new();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddAuthorizationCore();
            context.Services.AddLocalization();
            context.Services.AddSingleton<IAuthorizationService>(new AllowAllAuthorizationService());
            apiConnection = new FlowNetworkObjectsNamingApiConn();
            context.Services.AddSingleton<ApiConnection>(apiConnection);
            context.Services.AddScoped<DomEventService>();
            context.Services.AddSingleton<GlobalConfig>(new SimulatedGlobalConfig());
            context.Services.AddSingleton<UserConfig>(new SimulatedUserConfig
            {
                User = { Roles = [Roles.Admin] }
            });
            context.Services.AddSingleton<AuthenticationStateProvider>(new FlowSettingsPagesAuthStateProvider(Roles.Admin));
            return context;
        }

        private static void SeedTranslations()
        {
            foreach (string key in new[]
            {
                "network_groups",
                "service_objects",
                "service_groups",
                "time_objects",
                "duplicate_objects",
                "flow_object",
                "management",
                "objects",
                "actions",
                "id",
                "name",
                "state",
                "show_in_request_module",
                "details",
                "uid",
                "search_name",
                "custom_objects",
                "create_custom_flow_object",
                "flow_objects",
                "edit_flow_object",
                "save",
                "cancel",
                "select",
                "no_duplicate_conflicts",
                "current",
                "type",
                "ip"
            })
            {
                SimulatedUserConfig.DummyTranslate.TryAdd(key, key);
            }
        }

        private static IRenderedComponent<TComponent> RenderPage<TComponent>(BunitContext context)
            where TComponent : Microsoft.AspNetCore.Components.IComponent
        {
            return context.Render<CascadingAuthenticationState>(parameters => parameters
                .AddChildContent<TComponent>())
                .FindComponent<TComponent>();
        }

        private static void SetMember(object instance, string memberName, object? value)
        {
            Type type = instance.GetType();
            PropertyInfo? property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null)
            {
                property.SetValue(instance, value);
                return;
            }

            FieldInfo? field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(instance, value);
                return;
            }

            throw new MissingFieldException(type.FullName, memberName);
        }

        private sealed class FlowSettingsPagesAuthStateProvider(params string[] roles) : AuthenticationStateProvider
        {
            private readonly ClaimsPrincipal principal = new(new ClaimsIdentity(
                Array.ConvertAll(roles, role => new Claim(ClaimTypes.Role, role)),
                authenticationType: "Test",
                nameType: ClaimTypes.Name,
                roleType: ClaimTypes.Role));

            public override Task<AuthenticationState> GetAuthenticationStateAsync()
            {
                return Task.FromResult(new AuthenticationState(principal));
            }
        }
    }

    internal sealed class FlowNetworkObjectsNamingApiConn : SimulatedApiConnection
    {
        public List<string> Queries { get; } = [];
        public List<string> UpdatedFlowObjectNames { get; } = [];

        private readonly FlowNwObject flowNwObject = new()
        {
            Id = 100,
            Name = "",
            IpStart = null,
            IpEnd = null,
            Hash = "hash-100",
            State = FlowState.Requested,
            ShowInRequestModule = false,
            Objects = []
        };

        private readonly Management localManagement = new()
        {
            Id = 10,
            Name = "A Management",
            Objects =
            [
                new NetworkObject
                {
                    Id = 1,
                    Name = "",
                    IP = "",
                    IpEnd = "",
                    Uid = "local-1",
                    Active = true,
                    FlowNetworkObjectId = 100,
                    FlowActive = false,
                    Type = new NetworkObjectType { Id = 1, Name = "host" }
                }
            ]
        };

        private readonly Management globalManagement = new()
        {
            Id = 20,
            Name = "Global Management",
            Objects =
            [
                new NetworkObject
                {
                    Id = 2,
                    Name = "Global Object Name",
                    IP = "",
                    IpEnd = "",
                    Uid = "global-1",
                    Active = true,
                    FlowNetworkObjectId = 100,
                    FlowActive = false,
                    Type = new NetworkObjectType { Id = 1, Name = "host" }
                }
            ]
        };

        public override Task<QueryResponseType> SendQueryAsync<QueryResponseType>(string query, object? variables = null, string? operationName = null, QueryChunkingOptions? chunkingOptions = null)
        {
            Queries.Add(query);
            if (query == FlowQueries.getFlowSelectableManagements)
            {
                return Task.FromResult((QueryResponseType)(object)new List<Management>
                {
                    localManagement,
                    globalManagement
                });
            }
            if (query == FlowQueries.getFlowNwObjectCatalog)
            {
                return Task.FromResult((QueryResponseType)(object)new List<FlowNwObject> { flowNwObject });
            }
            if (query == FlowQueries.getFlowCustomObjectCandidates)
            {
                return Task.FromResult((QueryResponseType)(object)new List<Management> { localManagement });
            }
            if (query == FlowQueries.getFlowCustomObjectNamingCandidates)
            {
                return Task.FromResult((QueryResponseType)(object)new List<Management>
                {
                    localManagement,
                    globalManagement
                });
            }
            if (query == FlowMutations.updateFlowNwObject && typeof(QueryResponseType) == typeof(FlowNwObject))
            {
                string name = GetAnonymousProperty<string>(variables, "name");
                UpdatedFlowObjectNames.Add(name);
                return Task.FromResult((QueryResponseType)(object)new FlowNwObject
                {
                    Id = flowNwObject.Id,
                    Name = name,
                    IpStart = null,
                    IpEnd = null,
                    Hash = flowNwObject.Hash,
                    State = flowNwObject.State,
                    ShowInRequestModule = flowNwObject.ShowInRequestModule,
                    Objects = []
                });
            }
            if (query == ConfigQueries.upsertConfigItems)
            {
                return Task.FromResult((QueryResponseType)(object)new object());
            }

            throw new InvalidOperationException($"Unexpected query: {query}");
        }

        private static T GetAnonymousProperty<T>(object? variables, string propertyName)
        {
            if (variables == null)
            {
                throw new InvalidOperationException($"Missing variables for {propertyName}");
            }

            return (T)(variables.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(variables)
                ?? throw new InvalidOperationException($"Missing property {propertyName}"));
        }
    }

    internal sealed class FlowSettingsPagesTestApiConn : SimulatedApiConnection
    {
        private static readonly FlowSvcObject kFlowSvcObject = new()
        {
            Id = 100,
            Name = "Flow Service Object",
            PortStart = 80,
            PortEnd = 80,
            ProtoId = 6,
            State = FlowState.Requested,
            ShowInRequestModule = true
        };

        private static readonly FlowSvcGroup kFlowSvcGroup = new()
        {
            Id = 200,
            Name = "Flow Service Group",
            State = FlowState.Requested,
            ShowInRequestModule = true,
            SvcGroupMembers = [new FlowSvcGroupMember()]
        };

        private static readonly FlowNwGroup kFlowNwGroup = new()
        {
            Id = 300,
            Name = "Flow Network Group",
            State = FlowState.Requested,
            ShowInRequestModule = true,
            NwGroupMembers = [new FlowNwGroupMember(), new FlowNwGroupMember()]
        };

        private static readonly FlowTimeObject kFlowTimeObject = new()
        {
            Id = 400,
            Name = "Flow Time Object",
            StartTime = new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 5, 1, 18, 0, 0, DateTimeKind.Utc),
            State = FlowState.Requested,
            ShowInRequestModule = true
        };

        private static readonly Management kManagement = new()
        {
            Id = 10,
            Name = "Management"
        };

        private static readonly List<IpProtocol> kIpProtocols =
        [
            new() { Id = 1, Name = "ICMP" },
            new() { Id = 6, Name = "TCP" },
            new() { Id = 17, Name = "UDP" }
        ];

        private static readonly Management kServiceManagement = new()
        {
            Id = 10,
            Name = "Management",
            Services =
            [
                new()
                {
                    Id = 11,
                    Name = "Service A",
                    Uid = "svc-a",
                    DestinationPort = 80,
                    ProtoId = 6,
                    FlowServiceObjectId = 100,
                    FlowServiceGroupId = 200,
                    FlowActive = false
                },
                new()
                {
                    Id = 12,
                    Name = "Service B",
                    Uid = "svc-b",
                    DestinationPort = 80,
                    ProtoId = 6,
                    FlowServiceObjectId = 100,
                    FlowServiceGroupId = 200,
                    FlowActive = false
                }
            ]
        };

        private static readonly Management kNetworkManagement = new()
        {
            Id = 10,
            Name = "Management",
            Objects =
            [
                new()
                {
                    Id = 21,
                    Name = "Object A",
                    Uid = "obj-a",
                    IP = "10.0.0.1/32",
                    FlowNetworkGroupId = 300,
                    FlowActive = false
                },
                new()
                {
                    Id = 22,
                    Name = "Object B",
                    Uid = "obj-b",
                    IP = "10.0.0.2/32",
                    FlowNetworkGroupId = 300,
                    FlowActive = false
                }
            ]
        };

        private static readonly Management kTimeManagement = new()
        {
            Id = 10,
            Name = "Management",
            TimeObjects =
            [
                new()
                {
                    Id = 31,
                    Name = "Time A",
                    Uid = "time-a",
                    StartTime = new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc),
                    EndTime = new DateTime(2026, 5, 1, 18, 0, 0, DateTimeKind.Utc),
                    FlowTimeObjectId = 400,
                    FlowActive = false
                },
                new()
                {
                    Id = 32,
                    Name = "Time B",
                    Uid = "time-b",
                    StartTime = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc),
                    EndTime = new DateTime(2026, 5, 1, 19, 0, 0, DateTimeKind.Utc),
                    FlowTimeObjectId = 400,
                    FlowActive = false
                }
            ]
        };

        public override Task<QueryResponseType> SendQueryAsync<QueryResponseType>(string query, object? variables = null, string? operationName = null, QueryChunkingOptions? chunkingOptions = null)
        {
            object result = query switch
            {
                string q when q == FlowQueries.getFlowServiceObjects => new List<FlowSvcObject> { kFlowSvcObject },
                string q when q == FlowQueries.getFlowServiceGroups => new List<FlowSvcGroup> { kFlowSvcGroup },
                string q when q == FlowQueries.getFlowAddressGroups => new List<FlowNwGroup> { kFlowNwGroup },
                string q when q == FlowQueries.getFlowTimeObjects => new List<FlowTimeObject> { kFlowTimeObject },
                string q when q == StmQueries.getIpProtocols => new List<IpProtocol>(kIpProtocols),
                string q when q == FlowQueries.getFlowSelectableManagements => new List<Management> { kManagement },
                string q when q == FlowQueries.getFlowCustomServiceCandidates => new List<Management> { kServiceManagement },
                string q when q == FlowQueries.getFlowCustomObjectCandidates => new List<Management> { kNetworkManagement },
                string q when q == FlowQueries.getFlowCustomTimeObjectCandidates => new List<Management> { kTimeManagement },
                _ => throw new InvalidOperationException($"Unexpected query: {query}")
            };

            return Task.FromResult((QueryResponseType)result);
        }
    }

    internal sealed class FlowServiceObjectsCustomCreateApiConn : SimulatedApiConnection
    {
        private static readonly List<IpProtocol> kIpProtocols =
        [
            new() { Id = 1, Name = "ICMP" },
            new() { Id = 6, Name = "TCP" },
            new() { Id = 17, Name = "UDP" }
        ];

        public List<string> Queries { get; } = [];
        public FlowSvcObjectInsert? InsertedServiceObject { get; private set; }
        public List<(long ServiceId, long FlowSvcobjId, bool ActiveOnMgm)> MappingCalls { get; } = [];

        private readonly FlowSvcObject flowSvcObject = new()
        {
            Id = 100,
            Name = "Flow Service Object",
            PortStart = 80,
            PortEnd = 80,
            ProtoId = 6,
            State = FlowState.Requested,
            ShowInRequestModule = true
        };

        private readonly Management managementOne = new()
        {
            Id = 10,
            Name = "Management",
            Services =
            [
                new()
                {
                    Id = 11,
                    Name = "Service A",
                    Uid = "svc-a",
                    DestinationPort = 80,
                    DestinationPortEnd = 80,
                    ProtoId = 6,
                    FlowServiceObjectId = null,
                    Type = new NetworkServiceType { Name = ServiceType.SimpleService },
                    FlowActive = false
                },
                new()
                {
                    Id = 12,
                    Name = "Service Group Candidate",
                    Uid = "svc-group",
                    Type = new NetworkServiceType { Name = ServiceType.Group },
                    FlowServiceGroupId = null,
                    FlowActive = false
                }
            ]
        };

        private readonly Management managementTwo = new()
        {
            Id = 20,
            Name = "Management 2",
            Services =
            [
                new()
                {
                    Id = 21,
                    Name = "Service B",
                    Uid = "svc-b",
                    DestinationPort = 443,
                    DestinationPortEnd = 443,
                    ProtoId = 6,
                    FlowServiceObjectId = null,
                    Type = new NetworkServiceType { Name = ServiceType.SimpleService },
                    FlowActive = false
                }
            ]
        };

        public override Task<QueryResponseType> SendQueryAsync<QueryResponseType>(string query, object? variables = null, string? operationName = null, QueryChunkingOptions? chunkingOptions = null)
        {
            Queries.Add(query);
            if (query == FlowQueries.getFlowServiceObjects)
            {
                return Task.FromResult((QueryResponseType)(object)new List<FlowSvcObject> { flowSvcObject });
            }
            if (query == FlowQueries.getFlowSelectableManagements)
            {
                return Task.FromResult((QueryResponseType)(object)new List<Management>
                {
                    new() { Id = 10, Name = "Management" },
                    new() { Id = 20, Name = "Management 2" }
                });
            }
            if (query == StmQueries.getIpProtocols)
            {
                return Task.FromResult((QueryResponseType)(object)new List<IpProtocol>(kIpProtocols));
            }
            if (query == FlowQueries.getFlowCustomServiceCandidates)
            {
                return Task.FromResult((QueryResponseType)(object)new List<Management> { managementOne, managementTwo });
            }
            if (query == FlowQueries.insertFlowSvcObjects && typeof(QueryResponseType) == typeof(FlowSvcObjectInsertResult))
            {
                object?[] insertedObjects = GetAnonymousArray(variables, "objects");
                object? firstObject = insertedObjects.FirstOrDefault();
                InsertedServiceObject = new FlowSvcObjectInsert
                {
                    Name = GetAnonymousProperty<string>(firstObject, "Name"),
                    PortStart = GetAnonymousNullableProperty<int>(firstObject, "PortStart"),
                    PortEnd = GetAnonymousNullableProperty<int>(firstObject, "PortEnd"),
                    IpProtoId = GetAnonymousProperty<int>(firstObject, "IpProtoId"),
                    SvcObjHash = GetAnonymousProperty<string>(firstObject, "SvcObjHash"),
                    State = GetAnonymousProperty<string>(firstObject, "State"),
                    RemovedDate = null,
                    ShowInRequestModule = GetAnonymousProperty<bool>(firstObject, "ShowInRequestModule")
                };
                return Task.FromResult((QueryResponseType)(object)new FlowSvcObjectInsertResult
                {
                    Returning =
                    [
                        new FlowSvcObject
                        {
                            Id = 900,
                            Name = InsertedServiceObject.Name ?? "",
                            PortStart = InsertedServiceObject.PortStart,
                            PortEnd = InsertedServiceObject.PortEnd,
                            ProtoId = InsertedServiceObject.IpProtoId,
                            Hash = InsertedServiceObject.SvcObjHash ?? "",
                            State = InsertedServiceObject.State ?? FlowState.Implemented,
                            ShowInRequestModule = InsertedServiceObject.ShowInRequestModule
                        }
                    ]
                });
            }
            if (query == FlowMutations.upsertFlowSvcObjectMapping && typeof(QueryResponseType) == typeof(NetworkService))
            {
                long serviceId = GetAnonymousProperty<long>(variables, "svcId");
                long flowSvcobjId = GetAnonymousProperty<long>(variables, "flowSvcobjId");
                bool activeOnMgm = GetAnonymousProperty<bool>(variables, "activeOnMgm");
                MappingCalls.Add((serviceId, flowSvcobjId, activeOnMgm));
                return Task.FromResult((QueryResponseType)(object)new NetworkService
                {
                    Id = serviceId,
                    Name = serviceId == 11 ? "Service A" : "Service B",
                    Uid = serviceId == 11 ? "svc-a" : "svc-b",
                    DestinationPort = serviceId == 11 ? 80 : 443,
                    DestinationPortEnd = serviceId == 11 ? 80 : 443,
                    Active = true,
                    Removed = null,
                    FlowServiceObjectId = flowSvcobjId,
                    FlowActive = activeOnMgm
                });
            }
            throw new InvalidOperationException($"Unexpected query: {query}");
        }

        private static T GetAnonymousProperty<T>(object? variables, string propertyName)
        {
            if (variables == null)
            {
                throw new InvalidOperationException($"Missing variables for {propertyName}");
            }

            return (T)(variables.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(variables)
                ?? throw new InvalidOperationException($"Missing property {propertyName}"));
        }

        private static T? GetAnonymousNullableProperty<T>(object? variables, string propertyName)
            where T : struct
        {
            if (variables == null)
            {
                throw new InvalidOperationException($"Missing variables for {propertyName}");
            }

            object? value = variables.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(variables);
            return value == null ? null : (T)value;
        }

        private static object?[] GetAnonymousArray(object? variables, string propertyName)
        {
            if (variables == null)
            {
                throw new InvalidOperationException($"Missing variables for {propertyName}");
            }

            return (object?[])(variables.GetType().GetProperty(propertyName)?.GetValue(variables)
                ?? throw new InvalidOperationException($"Missing property {propertyName}"));
        }
    }

    internal sealed class FlowServiceObjectsProtocolOnlyApiConn : SimulatedApiConnection
    {
        private static readonly List<IpProtocol> kIpProtocols =
        [
            new() { Id = 1, Name = "ICMP" },
            new() { Id = 6, Name = "TCP" },
            new() { Id = 17, Name = "UDP" }
        ];

        public List<string> Queries { get; } = [];
        public FlowSvcObjectInsert? InsertedServiceObject { get; private set; }
        public List<(long ServiceId, long FlowSvcobjId, bool ActiveOnMgm)> MappingCalls { get; } = [];

        private readonly FlowSvcObject flowSvcObject = new()
        {
            Id = 100,
            Name = "Flow Service Object",
            State = FlowState.Requested,
            ShowInRequestModule = true
        };

        private readonly Management management = new()
        {
            Id = 10,
            Name = "Management",
            Services =
            [
                new()
                {
                    Id = 11,
                    Name = "Protocol Only Service",
                    Uid = "svc-proto-only",
                    DestinationPort = null,
                    DestinationPortEnd = null,
                    ProtoId = 1,
                    FlowServiceObjectId = null,
                    FlowActive = false
                }
            ]
        };

        public override Task<QueryResponseType> SendQueryAsync<QueryResponseType>(string query, object? variables = null, string? operationName = null, QueryChunkingOptions? chunkingOptions = null)
        {
            Queries.Add(query);
            if (query == FlowQueries.getFlowServiceObjects)
            {
                return Task.FromResult((QueryResponseType)(object)new List<FlowSvcObject> { flowSvcObject });
            }
            if (query == FlowQueries.getFlowSelectableManagements)
            {
                return Task.FromResult((QueryResponseType)(object)new List<Management> { new() { Id = 10, Name = "Management" } });
            }
            if (query == StmQueries.getIpProtocols)
            {
                return Task.FromResult((QueryResponseType)(object)new List<IpProtocol>(kIpProtocols));
            }
            if (query == FlowQueries.getFlowCustomServiceCandidates)
            {
                return Task.FromResult((QueryResponseType)(object)new List<Management> { management });
            }
            if (query == FlowQueries.insertFlowSvcObjects && typeof(QueryResponseType) == typeof(FlowSvcObjectInsertResult))
            {
                object?[] insertedObjects = GetAnonymousArray(variables, "objects");
                object? firstObject = insertedObjects.FirstOrDefault();
                InsertedServiceObject = new FlowSvcObjectInsert
                {
                    Name = GetAnonymousProperty<string>(firstObject, "Name"),
                    PortStart = GetAnonymousNullableProperty<int>(firstObject, "PortStart"),
                    PortEnd = GetAnonymousNullableProperty<int>(firstObject, "PortEnd"),
                    IpProtoId = GetAnonymousProperty<int>(firstObject, "IpProtoId"),
                    SvcObjHash = GetAnonymousProperty<string>(firstObject, "SvcObjHash"),
                    State = GetAnonymousProperty<string>(firstObject, "State"),
                    RemovedDate = null,
                    ShowInRequestModule = GetAnonymousProperty<bool>(firstObject, "ShowInRequestModule")
                };
                return Task.FromResult((QueryResponseType)(object)new FlowSvcObjectInsertResult
                {
                    Returning =
                    [
                        new FlowSvcObject
                        {
                            Id = 900,
                            Name = InsertedServiceObject.Name ?? "",
                            PortStart = InsertedServiceObject.PortStart,
                            PortEnd = InsertedServiceObject.PortEnd,
                            ProtoId = InsertedServiceObject.IpProtoId,
                            Hash = InsertedServiceObject.SvcObjHash ?? "",
                            State = InsertedServiceObject.State ?? FlowState.Implemented,
                            ShowInRequestModule = InsertedServiceObject.ShowInRequestModule
                        }
                    ]
                });
            }
            if (query == FlowMutations.upsertFlowSvcObjectMapping && typeof(QueryResponseType) == typeof(NetworkService))
            {
                long serviceId = GetAnonymousProperty<long>(variables, "svcId");
                long flowSvcobjId = GetAnonymousProperty<long>(variables, "flowSvcobjId");
                bool activeOnMgm = GetAnonymousProperty<bool>(variables, "activeOnMgm");
                MappingCalls.Add((serviceId, flowSvcobjId, activeOnMgm));
                return Task.FromResult((QueryResponseType)(object)new NetworkService
                {
                    Id = serviceId,
                    Name = "Protocol Only Service",
                    Uid = "svc-proto-only",
                    DestinationPort = null,
                    DestinationPortEnd = null,
                    Active = true,
                    Removed = null,
                    FlowServiceObjectId = flowSvcobjId,
                    FlowActive = activeOnMgm
                });
            }
            throw new InvalidOperationException($"Unexpected query: {query}");
        }

        private static T GetAnonymousProperty<T>(object? variables, string propertyName)
        {
            if (variables == null)
            {
                throw new InvalidOperationException($"Missing variables for {propertyName}");
            }

            return (T)(variables.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(variables)
                ?? throw new InvalidOperationException($"Missing property {propertyName}"));
        }

        private static T? GetAnonymousNullableProperty<T>(object? variables, string propertyName)
            where T : struct
        {
            if (variables == null)
            {
                throw new InvalidOperationException($"Missing variables for {propertyName}");
            }

            object? value = variables.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(variables);
            return value == null ? null : (T)value;
        }

        private static object?[] GetAnonymousArray(object? variables, string propertyName)
        {
            if (variables == null)
            {
                throw new InvalidOperationException($"Missing variables for {propertyName}");
            }

            return (object?[])(variables.GetType().GetProperty(propertyName)?.GetValue(variables)
                ?? throw new InvalidOperationException($"Missing property {propertyName}"));
        }
    }

    internal sealed class FlowServiceObjectsDuplicateResolverApiConn : SimulatedApiConnection
    {
        private static readonly List<IpProtocol> kIpProtocols =
        [
            new() { Id = 1, Name = "ICMP" },
            new() { Id = 6, Name = "TCP" },
            new() { Id = 17, Name = "UDP" }
        ];

        public List<string> Queries { get; } = [];
        public List<(long ServiceId, long FlowSvcobjId, bool ActiveOnMgm)> MappingCalls { get; } = [];
        public FlowSvcObjectInsert? InsertedServiceObject { get; private set; }

        private readonly FlowSvcObject flowSvcObject = new()
        {
            Id = 100,
            Name = "Flow Service Object",
            PortStart = 80,
            PortEnd = 80,
            ProtoId = 6,
            State = FlowState.Requested,
            ShowInRequestModule = true
        };

        private readonly Management management = new()
        {
            Id = 10,
            Name = "Management",
            Services =
            [
                new()
                {
                    Id = 11,
                    Name = "Service A",
                    Uid = "svc-a",
                    DestinationPort = 80,
                    DestinationPortEnd = 80,
                    ProtoId = 6,
                    FlowServiceObjectId = 100,
                    FlowActive = false
                },
                new()
                {
                    Id = 12,
                    Name = "Service B",
                    Uid = "svc-b",
                    DestinationPort = 80,
                    DestinationPortEnd = 80,
                    ProtoId = 6,
                    FlowServiceObjectId = 100,
                    FlowActive = false
                }
            ]
        };

        public override Task<QueryResponseType> SendQueryAsync<QueryResponseType>(string query, object? variables = null, string? operationName = null, QueryChunkingOptions? chunkingOptions = null)
        {
            Queries.Add(query);
            if (query == FlowQueries.getFlowServiceObjects)
            {
                return Task.FromResult((QueryResponseType)(object)new List<FlowSvcObject> { flowSvcObject });
            }
            if (query == FlowQueries.getFlowSelectableManagements)
            {
                return Task.FromResult((QueryResponseType)(object)new List<Management> { new() { Id = 10, Name = "Management" } });
            }
            if (query == StmQueries.getIpProtocols)
            {
                return Task.FromResult((QueryResponseType)(object)new List<IpProtocol>(kIpProtocols));
            }
            if (query == FlowQueries.getFlowCustomServiceCandidates)
            {
                return Task.FromResult((QueryResponseType)(object)new List<Management> { management });
            }
            if (query == FlowQueries.insertFlowSvcObjects && typeof(QueryResponseType) == typeof(FlowSvcObjectInsertResult))
            {
                object?[] insertedObjects = GetAnonymousArray(variables, "objects");
                object? firstObject = insertedObjects.FirstOrDefault();
                InsertedServiceObject = new FlowSvcObjectInsert
                {
                    Name = GetAnonymousProperty<string>(firstObject, "Name"),
                    PortStart = GetAnonymousNullableProperty<int>(firstObject, "PortStart"),
                    PortEnd = GetAnonymousNullableProperty<int>(firstObject, "PortEnd"),
                    IpProtoId = GetAnonymousProperty<int>(firstObject, "IpProtoId"),
                    SvcObjHash = GetAnonymousProperty<string>(firstObject, "SvcObjHash"),
                    State = GetAnonymousProperty<string>(firstObject, "State"),
                    RemovedDate = null,
                    ShowInRequestModule = GetAnonymousProperty<bool>(firstObject, "ShowInRequestModule")
                };
                return Task.FromResult((QueryResponseType)(object)new FlowSvcObjectInsertResult
                {
                    Returning =
                    [
                        new FlowSvcObject
                        {
                            Id = 900,
                            Name = InsertedServiceObject.Name ?? "",
                            PortStart = InsertedServiceObject.PortStart,
                            PortEnd = InsertedServiceObject.PortEnd,
                            ProtoId = InsertedServiceObject.IpProtoId,
                            Hash = InsertedServiceObject.SvcObjHash ?? "",
                            State = InsertedServiceObject.State ?? FlowState.Implemented,
                            ShowInRequestModule = InsertedServiceObject.ShowInRequestModule
                        }
                    ]
                });
            }
            if (query == FlowMutations.upsertFlowSvcObjectMapping && typeof(QueryResponseType) == typeof(NetworkService))
            {
                long serviceId = GetAnonymousProperty<long>(variables, "svcId");
                long flowSvcobjId = GetAnonymousProperty<long>(variables, "flowSvcobjId");
                bool activeOnMgm = GetAnonymousProperty<bool>(variables, "activeOnMgm");
                MappingCalls.Add((serviceId, flowSvcobjId, activeOnMgm));
                return Task.FromResult((QueryResponseType)(object)new NetworkService
                {
                    Id = serviceId,
                    Name = serviceId == 11 ? "Service A" : "Service B",
                    Uid = serviceId == 11 ? "svc-a" : "svc-b",
                    DestinationPort = 80,
                    DestinationPortEnd = 80,
                    Active = true,
                    Removed = null,
                    FlowServiceObjectId = flowSvcobjId,
                    FlowActive = activeOnMgm
                });
            }

            throw new InvalidOperationException($"Unexpected query: {query}");
        }

        private static T GetAnonymousProperty<T>(object? variables, string propertyName)
        {
            if (variables == null)
            {
                throw new InvalidOperationException($"Missing variables for {propertyName}");
            }

            return (T)(variables.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(variables)
                ?? throw new InvalidOperationException($"Missing property {propertyName}"));
        }

        private static T? GetAnonymousNullableProperty<T>(object? variables, string propertyName)
            where T : struct
        {
            if (variables == null)
            {
                throw new InvalidOperationException($"Missing variables for {propertyName}");
            }

            object? value = variables.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(variables);
            return value == null ? null : (T)value;
        }

        private static object?[] GetAnonymousArray(object? variables, string propertyName)
        {
            if (variables == null)
            {
                throw new InvalidOperationException($"Missing variables for {propertyName}");
            }

            return (object?[])(variables.GetType().GetProperty(propertyName)?.GetValue(variables)
                ?? throw new InvalidOperationException($"Missing property {propertyName}"));
        }
    }

    internal sealed class FlowNetworkObjectsDuplicateResolverApiConn : SimulatedApiConnection
    {
        public List<string> Queries { get; } = [];
        public List<(long ObjectId, long FlowNwobjId, bool ActiveOnMgm)> MappingCalls { get; } = [];

        private readonly FlowNwObject flowNwObject = new()
        {
            Id = 100,
            Name = "Flow Object",
            IpStart = null,
            IpEnd = null,
            Hash = "hash-100",
            State = FlowState.Implemented,
            ShowInRequestModule = false
        };

        private readonly Management management = new()
        {
            Id = 10,
            Name = "Management",
            Objects =
            [
                new NetworkObject
                {
                    Id = 11,
                    Name = "Object A",
                    IP = "",
                    IpEnd = "",
                    Uid = "obj-a",
                    Active = true,
                    Type = new NetworkObjectType { Id = 1, Name = "host" },
                    FlowNetworkObjectId = 100,
                    FlowActive = false
                },
                new NetworkObject
                {
                    Id = 12,
                    Name = "Object B",
                    IP = "",
                    IpEnd = "",
                    Uid = "obj-b",
                    Active = true,
                    Type = new NetworkObjectType { Id = 1, Name = "host" },
                    FlowNetworkObjectId = 100,
                    FlowActive = false
                }
            ]
        };

        public override Task<QueryResponseType> SendQueryAsync<QueryResponseType>(string query, object? variables = null, string? operationName = null, QueryChunkingOptions? chunkingOptions = null)
        {
            Queries.Add(query);
            if (query == FlowQueries.getFlowSelectableManagements)
            {
                return Task.FromResult((QueryResponseType)(object)new List<Management> { new() { Id = 10, Name = "Management" } });
            }
            if (query == FlowQueries.getFlowNwObjectCatalog)
            {
                return Task.FromResult((QueryResponseType)(object)new List<FlowNwObject> { flowNwObject });
            }
            if (query == FlowQueries.getFlowCustomObjectCandidates)
            {
                return Task.FromResult((QueryResponseType)(object)new List<Management> { management });
            }
            if (query == FlowQueries.getFlowCustomObjectNamingCandidates)
            {
                return Task.FromResult((QueryResponseType)(object)new List<Management> { management });
            }
            if (query == FlowMutations.upsertFlowNwObjectMapping && typeof(QueryResponseType) == typeof(NetworkObject))
            {
                long objectId = GetAnonymousProperty<long>(variables, "objId");
                long flowNwobjId = GetAnonymousProperty<long>(variables, "flowNwobjId");
                bool activeOnMgm = GetAnonymousProperty<bool>(variables, "activeOnMgm");
                MappingCalls.Add((objectId, flowNwobjId, activeOnMgm));
                return Task.FromResult((QueryResponseType)(object)new NetworkObject
                {
                    Id = objectId,
                    Name = objectId == 11 ? "Object A" : "Object B",
                    IP = "",
                    IpEnd = "",
                    Uid = objectId == 11 ? "obj-a" : "obj-b",
                    Active = true,
                    Removed = null,
                    FlowNetworkObjectId = flowNwobjId,
                    FlowActive = activeOnMgm
                });
            }

            throw new InvalidOperationException($"Unexpected query: {query}");
        }

        private static T GetAnonymousProperty<T>(object? variables, string propertyName)
        {
            if (variables == null)
            {
                throw new InvalidOperationException($"Missing variables for {propertyName}");
            }

            return (T)(variables.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(variables)
                ?? throw new InvalidOperationException($"Missing property {propertyName}"));
        }
    }
}
