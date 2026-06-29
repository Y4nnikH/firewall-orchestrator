using FWO.Api.Client.Queries;
using FWO.Data;
using FWO.Data.Workflow;
using FWO.Ui.Pages.Settings;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static FWO.Test.WorkflowConfigurationComponentTestSupport;

namespace FWO.Test
{
    [TestFixture]
    internal class AddWorkflowConfigurationComponentTest
    {
        [Test]
        public void CanSave_RequiresSourceAndNonBlankUniqueName()
        {
            AddWorkflowConfiguration component = new();
            SetField(component, "name", "Candidate");
            Assert.That(GetProperty<bool>(component, "CanSave"), Is.False);

            SetProperty(component, nameof(AddWorkflowConfiguration.SourceConfiguration), new WorkflowConfiguration { Id = 1, Name = "Source" });
            SetField(component, "name", "   ");
            Assert.That(GetProperty<bool>(component, "CanSave"), Is.False);

            SetProperty(component, nameof(AddWorkflowConfiguration.ExistingConfigurations), new List<WorkflowConfiguration>
            {
                new() { Id = 2, Name = "Candidate" }
            });
            SetField(component, "name", " candidate ");
            Assert.That(GetProperty<bool>(component, "CanSave"), Is.False);

            SetField(component, "name", "Independent");
            Assert.That(GetProperty<bool>(component, "CanSave"), Is.True);
        }

        [Test]
        public void OnParametersSet_ResetsInputsOnlyWhenPopupOpens()
        {
            AddWorkflowConfiguration component = new();
            SetField(component, "name", "Old");
            SetField(component, "description", "Old description");
            SetProperty(component, nameof(AddWorkflowConfiguration.Display), true);

            Invoke(component, "OnParametersSet");

            Assert.Multiple(() =>
            {
                Assert.That(GetField<string>(component, "name"), Is.Empty);
                Assert.That(GetFieldValue(component, "description"), Is.Null);
            });

            SetField(component, "name", "In progress");
            Invoke(component, "OnParametersSet");
            Assert.That(GetField<string>(component, "name"), Is.EqualTo("In progress"));
        }

        [Test]
        public void BuildClonedPhaseMapping_CopiesAllPhaseValuesAndNestedMappings()
        {
            WorkflowConfigurationPhase source = new()
            {
                TaskType = "generic",
                Phase = "approval",
                PhaseMatrix = new StateMatrixPhase
                {
                    Phase = "approval",
                    Active = true,
                    LowestInputState = 10,
                    LowestStartState = 20,
                    LowestEndState = 30,
                    DerivedStates = [new() { FromStateId = 11, DerivedStateId = 21 }],
                    TransitionGroups = [new() { TransitionGroupId = 7, SortOrder = 4 }]
                }
            };

            JObject result = JObject.FromObject(InvokeStatic("BuildClonedPhaseMapping", source, "Clone")!);
            JToken data = result["state_matrix_phase"]!["data"]!;

            Assert.Multiple(() =>
            {
                Assert.That((string?)result["task_type"], Is.EqualTo("generic"));
                Assert.That((string?)result["phase"], Is.EqualTo("approval"));
                Assert.That((string?)data["name"], Is.EqualTo("Clone::generic::approval"));
                Assert.That((bool?)data["active"], Is.True);
                Assert.That((int?)data["lowest_input_state"], Is.EqualTo(10));
                Assert.That((int?)data["lowest_start_state"], Is.EqualTo(20));
                Assert.That((int?)data["lowest_end_state"], Is.EqualTo(30));
                Assert.That((int?)data["state_matrix_derived_states"]?["data"]?[0]?["derived_state_id"], Is.EqualTo(21));
                Assert.That((int?)data["state_matrix_phase_transition_groups"]?["data"]?[0]?["transition_group_id"], Is.EqualTo(7));
                Assert.That((int?)data["state_matrix_phase_transition_groups"]?["data"]?[0]?["sort_order"], Is.EqualTo(4));
                Assert.That(result["phase_matrix_id"], Is.Null);
            });
        }

        [Test]
        public void BuildClonedPhaseMapping_PreservesEmptyNestedCollections()
        {
            WorkflowConfigurationPhase source = new()
            {
                TaskType = "master",
                Phase = "request",
                PhaseMatrix = new StateMatrixPhase { Phase = "request" }
            };

            JObject result = JObject.FromObject(InvokeStatic("BuildClonedPhaseMapping", source, "Empty")!);
            JToken data = result["state_matrix_phase"]!["data"]!;

            Assert.Multiple(() =>
            {
                Assert.That(data["state_matrix_derived_states"]?["data"], Is.Empty);
                Assert.That(data["state_matrix_phase_transition_groups"]?["data"], Is.Empty);
            });
        }

        [Test]
        public async Task Save_TrimsPayloadClonesPhasesAndClosesPopup()
        {
            WorkflowConfiguration sourceConfiguration = new() { Id = 5, Name = "Source" };
            WorkflowConfigurationPhase sourcePhase = new()
            {
                TaskType = "master",
                Phase = "request",
                PhaseMatrix = new StateMatrixPhase { Phase = "request", Active = true }
            };
            RecordingWorkflowApiConnection api = new();
            api.Respond(RequestQueries.getWorkflowConfigurationPhaseMappings, new List<WorkflowConfigurationPhase> { sourcePhase });
            api.Respond(RequestQueries.createWorkflowConfiguration, new ReturnId { NewId = 91 });
            AddWorkflowConfiguration component = new();
            SetProperty(component, "apiConnection", api);
            SetProperty(component, nameof(AddWorkflowConfiguration.SourceConfiguration), sourceConfiguration);
            SetProperty(component, nameof(AddWorkflowConfiguration.Display), true);
            SetField(component, "name", "  New config  ");
            SetField(component, "description", "  Description  ");

            await InvokeAsync(component, "Save");

            JObject lookupVariables = JObject.FromObject(api.Calls[0].Variables!);
            JObject createVariables = JObject.FromObject(api.Calls[1].Variables!);
            Assert.Multiple(() =>
            {
                Assert.That((int?)lookupVariables["configurationId"], Is.EqualTo(5));
                Assert.That((string?)createVariables["name"], Is.EqualTo("New config"));
                Assert.That((string?)createVariables["description"], Is.EqualTo("Description"));
                Assert.That((string?)createVariables["phaseMappings"]?[0]?["state_matrix_phase"]?["data"]?["name"], Is.EqualTo("New config::master::request"));
                Assert.That(GetProperty<bool>(component, nameof(AddWorkflowConfiguration.Display)), Is.False);
            });
        }

        private static object? InvokeStatic(string methodName, params object?[] parameters)
        {
            System.Reflection.MethodInfo method = typeof(AddWorkflowConfiguration).GetMethod(methodName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? throw new MissingMethodException(typeof(AddWorkflowConfiguration).FullName, methodName);
            return method.Invoke(null, parameters);
        }
    }
}
